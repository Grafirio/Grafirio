using Grafirio.Bridge.Desktop.Authentication;
using Grafirio.Bridge.Desktop.Cloud;

namespace Grafirio.Bridge.Desktop.Shell;

internal static class ConnectionContextRequest
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task<ConnectionContextResponse> ResolveAsync(string requestId,
        IDesktopSessionManager sessions, IDesktopBridgeController bridge, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (session is null) return new(requestId, Error: "signedOut");
        if (session.ExpiresAt is not { } expiry || expiry <= DateTimeOffset.UtcNow)
            return new(requestId, Error: "signedOut");
        var identity = DesktopBridgeIdentity.GetDirectory(session);
        if (!ReferenceEquals(sessions.Current, session)) return new(requestId, Error: "signedOut");

        var bridgeId = await bridge.GetConnectionContextAsync(session, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var current = sessions.Current;
        if (current is null || current.ExpiresAt is not { } currentExpiry || currentExpiry <= DateTimeOffset.UtcNow
            || !ReferenceEquals(current, session) || DesktopBridgeIdentity.GetDirectory(current) != identity)
            return new(requestId, Error: "signedOut");
        return bridgeId == Guid.Empty
            ? new(requestId, Error: "unavailable", Reason: "Bridge kimliği henüz alınmadı.")
            : new(requestId, bridgeId) { Session = session };
    }
}