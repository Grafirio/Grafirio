using Microsoft.Extensions.Configuration;
using Grafirio.Shared.Options;
using Refit;
using System.Text.Json.Serialization;

namespace Grafirio.Shared.Services
{
    public interface IKeycloakUserService
    {
        Task<ServiceResult<string>> CreateUserAsync(CreateUserRequest request);
        Task<ServiceResult> AssignUserToCompanyAsync(string userId, Guid companyId, string role);
        Task<ServiceResult> UpdateUserCompanyAsync(string userId, Guid companyId, string newRole);
        Task<ServiceResult> RemoveUserFromCompanyAsync(string userId, Guid companyId);
    }

    public class CreateUserRequest
    {
        public required string Email { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Password { get; set; }
        public required Guid CompanyId { get; set; }
        public string Role { get; set; } = "COMPANY_USER";
        public bool EmailVerified { get; set; } = false;
        public bool RequirePasswordChange { get; set; } = true;
    }

    // Keycloak API DTOs
    public class KeycloakUserDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("emailVerified")]
        public bool EmailVerified { get; set; } = false;

        [JsonPropertyName("credentials")]
        public KeycloakCredentialDto[]? Credentials { get; set; }

        [JsonPropertyName("attributes")]
        public Dictionary<string, string[]>? Attributes { get; set; }
    }

    public class KeycloakCredentialDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "password";

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("temporary")]
        public bool Temporary { get; set; } = true;
    }

    // Keycloak Admin API Interface
    public interface IKeycloakAdminApi
    {
        [Post("/admin/realms/{realm}/users")]
        Task<HttpResponseMessage> CreateUserAsync(
            string realm,
            [Body] KeycloakUserDto user,
            [Header("Authorization")] string authorization);

        [Get("/admin/realms/{realm}/users")]
        Task<KeycloakUserDto[]> GetUsersAsync(
            string realm,
            [Query] string? email = null,
            [Header("Authorization")] string authorization = "");

        [Get("/admin/realms/{realm}/users/{userId}")]
        Task<KeycloakUserDto> GetUserAsync(
            string realm,
            string userId,
            [Header("Authorization")] string authorization);

        [Put("/admin/realms/{realm}/users/{userId}")]
        Task<HttpResponseMessage> UpdateUserAsync(
            string realm,
            string userId,
            [Body] KeycloakUserDto user,
            [Header("Authorization")] string authorization);

        [Post("/realms/{realm}/protocol/openid-connect/token")]
        Task<KeycloakTokenResponse> GetAdminTokenAsync(
            string realm,
            [Body(BodySerializationMethod.UrlEncoded)] Dictionary<string, object> credentials);
    }

    public class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    internal class KeycloakUserService : IKeycloakUserService
    {
        private readonly IKeycloakAdminApi _adminApi;
        private readonly string _realm;
        private readonly KeycloakAdminOptions _adminOptions;
        private string? _adminToken;

        public KeycloakUserService(IConfiguration configuration)
        {
            var identityOptions = configuration.GetSection("IdentityOption").Get<IdentityOption>();
            var adminOptions = configuration.GetSection("KeycloakAdmin").Get<KeycloakAdminOptions>();
            
            if (identityOptions == null)
                throw new InvalidOperationException("IdentityOption configuration is missing");
            if (adminOptions == null)
                throw new InvalidOperationException("KeycloakAdmin configuration is missing");
            
            _adminOptions = adminOptions;
            _realm = _adminOptions.Realm;
            _adminApi = RestService.For<IKeycloakAdminApi>(identityOptions.Address);
        }

        public async Task<ServiceResult<string>> CreateUserAsync(CreateUserRequest request)
        {
            try
            {
                await EnsureAdminTokenAsync();

                var user = new KeycloakUserDto
                {
                    Username = request.Email,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Enabled = true,
                    EmailVerified = request.EmailVerified,
                    Credentials = new[]
                    {
                        new KeycloakCredentialDto
                        {
                            Type = "password",
                            Value = request.Password,
                            Temporary = request.RequirePasswordChange
                        }
                    },
                    Attributes = new Dictionary<string, string[]>
                    {
                        ["company_id"] = new[] { request.CompanyId.ToString() },
                        ["accessible_companies"] = new[] { request.CompanyId.ToString() },
                        ["business_roles"] = new[] { request.Role }
                    }
                };

                var response = await _adminApi.CreateUserAsync(_realm, user, $"Bearer {_adminToken}");
                
                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<string>.Error("User creation failed", response.ReasonPhrase ?? "Unknown error", response.StatusCode);
                }

                // Get the created user to return the ID
                var createdUsers = await _adminApi.GetUsersAsync(_realm, request.Email, $"Bearer {_adminToken}");
                var createdUser = createdUsers?.FirstOrDefault();
                
                if (createdUser == null)
                {
                    return ServiceResult<string>.Error("User created but ID not found", System.Net.HttpStatusCode.InternalServerError);
                }

                // Extract user ID from location header or use email as fallback
                var userId = response.Headers.Location?.Segments.LastOrDefault() ?? createdUser.Username;
                
                return ServiceResult<string>.SuccessAsCreated(userId, $"/users/{userId}");
            }
            catch (Exception ex)
            {
                return ServiceResult<string>.Error("Keycloak user creation failed", ex.Message, System.Net.HttpStatusCode.InternalServerError);
            }
        }

        public async Task<ServiceResult> AssignUserToCompanyAsync(string userId, Guid companyId, string role)
        {
            try
            {
                await EnsureAdminTokenAsync();
                
                var user = await _adminApi.GetUserAsync(_realm, userId, $"Bearer {_adminToken}");
                
                // Update user attributes
                user.Attributes ??= new Dictionary<string, string[]>();
                user.Attributes["company_id"] = new[] { companyId.ToString() };
                
                // Add to accessible companies
                var accessibleCompanies = GetAccessibleCompanies(user);
                if (!accessibleCompanies.Contains(companyId.ToString()))
                {
                    accessibleCompanies.Add(companyId.ToString());
                    user.Attributes["accessible_companies"] = accessibleCompanies.ToArray();
                }

                // Add business role
                var businessRoles = GetBusinessRoles(user);
                if (!businessRoles.Contains(role))
                {
                    businessRoles.Add(role);
                    user.Attributes["business_roles"] = businessRoles.ToArray();
                }

                var response = await _adminApi.UpdateUserAsync(_realm, userId, user, $"Bearer {_adminToken}");
                
                return response.IsSuccessStatusCode 
                    ? ServiceResult.SuccessAsNoContent() 
                    : ServiceResult.Error("Failed to assign user to company", response.StatusCode);
            }
            catch (Exception ex)
            {
                return ServiceResult.Error("Keycloak user assignment failed", ex.Message, System.Net.HttpStatusCode.InternalServerError);
            }
        }

        public async Task<ServiceResult> UpdateUserCompanyAsync(string userId, Guid companyId, string newRole)
        {
            try
            {
                await EnsureAdminTokenAsync();
                
                var user = await _adminApi.GetUserAsync(_realm, userId, $"Bearer {_adminToken}");
                
                // Update business roles
                user.Attributes ??= new Dictionary<string, string[]>();
                user.Attributes["business_roles"] = new[] { newRole };

                var response = await _adminApi.UpdateUserAsync(_realm, userId, user, $"Bearer {_adminToken}");
                
                return response.IsSuccessStatusCode 
                    ? ServiceResult.SuccessAsNoContent() 
                    : ServiceResult.Error("Failed to update user company role", response.StatusCode);
            }
            catch (Exception ex)
            {
                return ServiceResult.Error("Keycloak user update failed", ex.Message, System.Net.HttpStatusCode.InternalServerError);
            }
        }

        public async Task<ServiceResult> RemoveUserFromCompanyAsync(string userId, Guid companyId)
        {
            try
            {
                await EnsureAdminTokenAsync();
                
                var user = await _adminApi.GetUserAsync(_realm, userId, $"Bearer {_adminToken}");
                
                user.Attributes ??= new Dictionary<string, string[]>();
                
                // Remove from accessible companies
                var accessibleCompanies = GetAccessibleCompanies(user);
                accessibleCompanies.Remove(companyId.ToString());
                user.Attributes["accessible_companies"] = accessibleCompanies.ToArray();

                // If this was the current company, clear it
                if (user.Attributes.ContainsKey("company_id") && 
                    user.Attributes["company_id"].FirstOrDefault() == companyId.ToString())
                {
                    user.Attributes.Remove("company_id");
                    user.Attributes.Remove("business_roles");
                }

                var response = await _adminApi.UpdateUserAsync(_realm, userId, user, $"Bearer {_adminToken}");
                
                return response.IsSuccessStatusCode 
                    ? ServiceResult.SuccessAsNoContent() 
                    : ServiceResult.Error("Failed to remove user from company", response.StatusCode);
            }
            catch (Exception ex)
            {
                return ServiceResult.Error("Keycloak user removal failed", ex.Message, System.Net.HttpStatusCode.InternalServerError);
            }
        }

        private async Task EnsureAdminTokenAsync()
        {
            if (string.IsNullOrEmpty(_adminToken))
            {
                var credentials = new Dictionary<string, object>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "admin-cli",
                    ["username"] = _adminOptions.Username,
                    ["password"] = _adminOptions.Password
                };

                var tokenResponse = await _adminApi.GetAdminTokenAsync(_realm, credentials);
                _adminToken = tokenResponse.AccessToken;
            }
        }

        private static List<string> GetAccessibleCompanies(KeycloakUserDto user)
        {
            if (user.Attributes?.ContainsKey("accessible_companies") == true)
            {
                return user.Attributes["accessible_companies"].ToList();
            }
            return new List<string>();
        }

        private static List<string> GetBusinessRoles(KeycloakUserDto user)
        {
            if (user.Attributes?.ContainsKey("business_roles") == true)
            {
                return user.Attributes["business_roles"].ToList();
            }
            return new List<string>();
        }
    }

    public class KeycloakAdminOptions
    {
        public required string Realm { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
    }
}