namespace Grafirio.Identity.Api.Features.Companies.GetCurrent;

public static class GetCurrentCompanyEndpoint
{
    public static RouteGroupBuilder GetCurrentCompanyGroupItemEndpoint(this RouteGroupBuilder group)
    {
        // Buradaki dönüş, gruptaki diğer uçların "her hatada BadRequest" kalıbını
        // bilerek izlemiyor. "Kullanıcı henüz bir şirkete bağlı değil" bu sayfada
        // hata değil, gösterilecek bir durum: panel onu 404'e bakarak ayırt edip
        // karşılama metnini gösteriyor.
        group.MapGet("/current", async ([FromQuery] Guid? companyId, IMediator mediator) =>
                (await mediator.Send(new GetCurrentCompanyQuery(companyId))).ToGenericResult())
            .WithName("GetCurrentCompany")
            .Produces<CurrentCompanyResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        return group;
    }
}
