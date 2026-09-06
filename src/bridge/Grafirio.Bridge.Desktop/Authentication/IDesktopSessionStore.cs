namespace Grafirio.Bridge.Desktop.Authentication;

public interface IDesktopSessionStore
{
    Task<UserSession?> LoadAsync(CancellationToken ct);
    Task SaveAsync(UserSession session, CancellationToken ct);
    Task DeleteAsync(CancellationToken ct);
}