namespace Grafirio.Bridge.Desktop.Authentication;

public interface IDesktopSessionManager
{
    UserSession? Current { get; }
    event Action? SessionChanged;
    Task RestoreAsync(CancellationToken ct);
    Task<UserSession?> SignInAsync(CancellationToken ct);
    Task<UserSession?> RefreshAsync(CancellationToken ct);
    Task<UserSession?> GetAsync(CancellationToken ct);
    Task SignOutAsync(CancellationToken ct);
}