using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.Shared.Identity.Services;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

/// <summary>
/// "Bu baglanti cagiranin sirketine mi ait?" — tek yerde.
///
/// Ayni dort satir bes ayri uc dosyasinda tekrar ediyordu ve tekrar ettikce
/// ayrisiyordu: bazisi sirketsiz hesaba 400, bazisi <c>Forbid()</c> donuyor,
/// bazisi <c>IsActive</c> suzgecini atliyordu. Ucu de ayni guvenlik kontrolu;
/// ayni soruya uc farkli cevap veren bir kod, er ya da gec yanlis olani secer.
///
/// Bulunamadi icin 404 donuyor, 403 degil: "bu kayit var ama senin degil"
/// bilgisi baska bir sirketin kayit kimligini dogrulamaya yarar.
/// </summary>
public static class ConnectionScope
{
    public static async Task<(SavedConnection? Connection, IResult? Error)> ResolveAsync(
        DataAnalysisDbContext db,
        IIdentityService identity,
        Guid connectionId,
        CancellationToken ct = default)
    {
        if (identity.CurrentCompanyId is not { } companyId)
        {
            return (null, Results.BadRequest(new { error = "Hesabınız bir firmaya bağlı değil" }));
        }

        var scopedCompanyId = companyId.ToString();

        var connection = await db.SavedConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId && c.CompanyId == scopedCompanyId && c.IsActive, ct);

        return connection is null
            ? (null, Results.NotFound(new { error = "Bağlantı bulunamadı" }))
            : (connection, null);
    }
}
