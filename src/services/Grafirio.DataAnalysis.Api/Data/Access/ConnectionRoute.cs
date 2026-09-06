using Grafirio.DataAnalysis.Api.Data.Entities;

namespace Grafirio.DataAnalysis.Api.Data.Access;

public sealed record ConnectionRoute(string ConnectionMode = ConnectionRoute.Direct, Guid? BridgeId = null)
{
    public const string Direct = "direct";
    public const string Bridge = "bridge";

    public string? Fingerprint { get; init; }
    public string? Revision { get; init; }
    public bool Pending { get; init; }
    public bool Failed { get; init; }

    public bool Matches(SavedConnection connection) =>
        !Pending && Revision is not null && Fingerprint == ConnectionRouteFingerprint.Create(connection);

    public void ValidateBinding(SavedConnection connection)
    {
        Validate();
        if (!Matches(connection))
            throw new DataSourceException("Bağlantı yönlendirmesi tamamlanmamış veya kayıtla eşleşmiyor. Bağlantıyı yeniden kaydedin.");
    }

    public void Validate()
    {
        if (Pending)
            throw new DataSourceException(Failed
                ? "Bağlantı güncellemesi tamamlanamadı; hedef ve parola seçerek yeniden kaydedin."
                : "Bağlantı güncelleniyor; yönlendirme henüz kullanılamaz.");
        if (ConnectionMode is not (Direct or Bridge))
            throw new DataSourceException("connectionMode must be 'direct' or 'bridge'.");
        if (ConnectionMode == Bridge && (BridgeId is null || BridgeId == Guid.Empty))
            throw new DataSourceException("Bridge bağlantısı için bridgeId gereklidir.");
        if (ConnectionMode == Direct && BridgeId is not null)
            throw new DataSourceException("Doğrudan bağlantıda bridgeId gönderilemez.");
    }
}