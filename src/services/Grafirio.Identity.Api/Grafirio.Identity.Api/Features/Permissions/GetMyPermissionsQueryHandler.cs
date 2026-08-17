using System.Net;

namespace Grafirio.Identity.Api.Features.Permissions;

public class GetMyPermissionsQueryHandler(IPermissionService permissions)
    : IRequestHandler<GetMyPermissionsQuery, ServiceResult<EffectivePermissions>>
{
    public async Task<ServiceResult<EffectivePermissions>> Handle(GetMyPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await permissions.ForCompanyAsync(request.CompanyId, cancellationToken);

        if (result.Role is null)
        {
            return ServiceResult<EffectivePermissions>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        return ServiceResult<EffectivePermissions>.SuccessAsOk(result);
    }
}
