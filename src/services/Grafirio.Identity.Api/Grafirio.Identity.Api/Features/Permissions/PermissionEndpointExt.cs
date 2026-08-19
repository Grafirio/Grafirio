using Grafirio.Identity.Api.Features.Permissions.UserAccess;
using Grafirio.Identity.Api.Features.Users;
using Asp.Versioning.Builder;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Permissions;

public static class PermissionEndpointExt
{
    public static void AddPermissionGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("api/v{version:apiVersion}/permissions")
            .WithTags("Permissions")
            .WithApiVersionSet(apiVersionSet);

        group.MapGet("/me/{companyId:guid}", async (Guid companyId, IMediator mediator) =>
                (await mediator.Send(new GetMyPermissionsQuery(companyId))).ToGenericResult())
            .WithName("GetMyPermissions")
            .Produces<EffectivePermissions>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        // Bir kisinin yetkisinin tamami tek parca: uyelik seviyesi, rolleri,
        // kisisel izinleri ve birlesimi. Panel "bu izin nereden geliyor"
        // sorusunu bundan cevapliyor.
        group.MapGet("/users/{companyId:guid}/{keycloakUserId}",
                async (Guid companyId, string keycloakUserId, IMediator mediator) =>
                    (await mediator.Send(new GetUserAccessQuery(companyId, keycloakUserId)))
                        .ToGenericResult())
            .WithName("GetUserAccess")
            .Produces<UserAccessDto>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        // Kisiye ozel izinler. Liste tam gonderiliyor: eksik gelen anahtar
        // kaldirilmis sayiliyor, aksi halde izin kaldirmanin ayri bir ucu
        // gerekirdi.
        group.MapPut("/users/{companyId:guid}/{keycloakUserId}",
                async (Guid companyId, string keycloakUserId,
                    SetUserPermissionsRequest request, IMediator mediator) =>
                    (await mediator.Send(new SetUserPermissionsCommand(
                        companyId, keycloakUserId, request.Permissions))).ToGenericResult())
            .WithName("SetUserPermissions")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");


        // Izin matrisinin satir ve sutunlari: hangi modulun hangi aksiyonlari
        // var. PANEL.READ listeye girmiyor — uyelikle geliyor, atanmiyor.
        group.MapGet("/actions", () => Results.Ok(
                AppModules.All.Select(module => new
                {
                    module,
                    permissions = AppPermissions.ForModule(module)
                })))
            .WithName("GetPermissionActions")
            .Produces(StatusCodes.Status200OK)
            .RequireAuthorization("Password");

        group.MapToApiVersion(1, 0);
    }
}
