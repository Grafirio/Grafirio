using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Departments.Members;

public class RemoveUserFromDepartmentCommandHandler(
    AppDbContext context,
    ICompanyAccessService access,
    IIdentityService identityService)
    : IRequestHandler<RemoveUserFromDepartmentCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(RemoveUserFromDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(x => x.Id == request.DepartmentId, cancellationToken);

        if (department is null)
        {
            return ServiceResult<bool>.Error("Department not found", HttpStatusCode.NotFound);
        }

        if (!await access.HasRoleAsync(department.CompanyId, CompanyRoles.COMPANY_ADMIN, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Departmandan kullanıcı çıkarmak için bu şirkette yönetici olmanız gerekiyor.",
                HttpStatusCode.Forbidden);
        }

        var membership = await context.UserDepartments.FirstOrDefaultAsync(
            x => x.DepartmentId == department.Id
                 && x.KeycloakUserId == request.KeycloakUserId
                 && x.IsActive,
            cancellationToken);

        if (membership is null)
        {
            return ServiceResult<bool>.Error("Membership not found", HttpStatusCode.NotFound);
        }

        // Silinmiyor, kapatiliyor — kimin ne zaman hangi departmanda oldugu
        // sonradan sorulabilsin (UserCompanyRole ile ayni desen).
        membership.IsActive = false;
        membership.RemovedAt = DateTime.UtcNow;
        membership.RemovedBy = identityService.UserName;

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}
