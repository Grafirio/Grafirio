namespace Grafirio.Identity.Api.Features.Users.Revoke;

/// Kişinin şirketteki üyeliğini kapatır. Kurucunun üyeliği kapatılamaz.
public record RevokeMembershipCommand(string KeycloakUserId, Guid CompanyId)
    : IRequestByServiceResult<bool>;
