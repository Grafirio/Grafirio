namespace Grafirio.Bridge.Desktop.Authentication;

public interface IBrowserLogin
{
    Task<UserSession?> TryLoginAsync(CancellationToken ct);
}