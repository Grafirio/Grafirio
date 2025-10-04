using MongoDB.Driver;
using Grafirio.Identity.Api.Features.Companies;
using Grafirio.Identity.Api.Features.Users;

namespace Grafirio.Identity.Api.Repositories
{
    public static class SeedData
    {
        public static async Task AddSeedDataExt(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.Database.AutoTransactionBehavior = Microsoft.EntityFrameworkCore.AutoTransactionBehavior.Never;

            if (!dbContext.Companies.Any())
            {
                var companies = new List<Company>
                {
                    new() { 
                        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), 
                        Name = "Grafirio Headquarters", 
                        Code = "HQ",
                        Description = "Main company headquarters",
                        Level = 0,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new() { 
                        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), 
                        Name = "Grafirio Istanbul Branch", 
                        Code = "IST",
                        Description = "Istanbul branch office",
                        ParentCompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Level = 1,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new() { 
                        Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), 
                        Name = "Grafirio Ankara Branch", 
                        Code = "ANK",
                        Description = "Ankara branch office",
                        ParentCompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Level = 1,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };

                dbContext.Companies.AddRange(companies);
                await dbContext.SaveChangesAsync();
            }

            // Sample user-company roles (bu test amaçlı, gerçekte Keycloak'tan gelecek)
            if (!dbContext.UserCompanyRoles.Any())
            {
                var userRoles = new List<UserCompanyRole>
                {
                    new() {
                        KeycloakUserId = "332ee8cd-f3f6-49fa-92e2-5fdb188b3377", // Test user from IdentityServiceFake
                        CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Role = CompanyRoles.COMPANY_ADMIN,
                        IsActive = true,
                        AssignedAt = DateTime.UtcNow
                    }
                };

                dbContext.UserCompanyRoles.AddRange(userRoles);
                await dbContext.SaveChangesAsync();
            }
        }
    }
}