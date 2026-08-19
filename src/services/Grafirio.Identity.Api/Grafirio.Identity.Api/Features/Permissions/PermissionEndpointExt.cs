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

        // Panelin modul secim kutularini doldurmasi icin; sabit listeyi
        // istemciye kopyalamak, iki tarafin sessizce ayrisma yolu olurdu.
        group.MapGet("/modules", () => Results.Ok(AppModules.All))
            .WithName("GetModules")
            .Produces<string[]>(StatusCodes.Status200OK)
            .RequireAuthorization("Password");

        // Departman izin matrisini cizmek icin: hangi modulun hangi aksiyonlari
        // var. PANEL.READ listeye girmiyor — departmana atanmiyor.
        group.MapGet("/actions", () => Results.Ok(
                AppModules.All.Select(module => new
                {
                    module,
                    permissions = AppPermissions.ForModule(module)
                })))
            .WithName("GetPermissionActions")
            .Produces(StatusCodes.Status200OK)
            .RequireAuthorization("Password");

        // Yetki Ayarlari ekranindaki "hangi rol neye izin veriyor" tablosu
        // onceden elle yazilmisti ve dosyanin kendi yorumu "yeni satir
        // eklenecekse once sunucuda karsiligi olmali" diyordu. Artik tablo
  
        group.MapToApiVersion(1, 0);
    }
}
