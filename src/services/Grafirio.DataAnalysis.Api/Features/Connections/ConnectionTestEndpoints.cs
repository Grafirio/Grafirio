using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Models;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

/// <summary>
/// Baglanti denemesi: kullanicinin girdigi bilgilerle gercekten baglanilabiliyor
/// mu diye bakar. Kayitli baglantilarin CRUD'u <see cref="ConnectionEndpoints"/>'te.
///
/// Onceden bu sinif da <c>ConnectionEndpoints</c> adiyla ayri bir
/// <c>Features.Connection</c> (tekil) ad alanindaydi. Iki ayni adli sinif ve
/// bir harf farkli iki klasor, hangi dosyanin hangi ucu kurdugunu okunamaz
/// yapiyordu; ayrica baglanti dizesini kuran yardimcilar da burada oldugu icin
/// "endpoint dosyasi" olmayan yerlerden cagriliyordu.
///
/// Baglanti dizesini kurma isi artik burada degil: <see cref="DataSourceTarget"/>
/// ve <see cref="IDataSourceFactory"/>. Bu dosya yalnizca ucu kuruyor.
/// </summary>
public static class ConnectionTestEndpoints
{
    public static void MapConnectionTestEndpoints(this IEndpointRouteBuilder app)
    {
        // Kimlik dogrulamasi zorunlu. Uc daha once aciktı: istekteki host'a
        // sunucu adina baglanti kuruyordu, yani kimligi olmayan biri bunu
        // ic aglari yoklamak icin kullanabilirdi.
        var group = app.MapGroup("/api/connections")
            .RequireAuthorization("CompanyAccess")
            .WithTags("Connection Management")
            .WithOpenApi();

        group.MapPost("/test", TestConnection)
            .WithName("TestConnection")
            .WithDescription("Verilen bilgilerle SQL Server bağlantısını dener");
    }

    private static async Task<IResult> TestConnection(
        SqlConnectionRequest request,
        IDataSourceFactory dataSources,
        CancellationToken ct)
    {
        var result = await dataSources.ProbeAsync(DataSourceTarget.From(request), ct);

        return Results.Ok(new TestConnectionResponse(
            Success: result.Success,
            Message: result.Message,
            // Kaydedilmemis bir denemenin kimligi yok; eski davranis korunuyor.
            ConnectionId: result.Success ? Guid.NewGuid().ToString() : null
        ));
    }
}
