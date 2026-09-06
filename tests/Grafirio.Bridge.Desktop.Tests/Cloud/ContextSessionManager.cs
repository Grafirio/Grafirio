using System.Text.Json;
using Grafirio.Bridge.Desktop.Authentication;

namespace Grafirio.Bridge.Desktop.Tests.Cloud;

internal sealed class ContextSessionManager : IDesktopSessionManager
{
    public UserSession? Current { get; private set; }
    public event Action? SessionChanged;
    public int GetCount { get; private set; }
    public Func<CancellationToken, Task<UserSession?>>? GetSession { get; set; }

    public void Set(UserSession? session)
    {
        Current = session;
        SessionChanged?.Invoke();
    }

    public Task RestoreAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<UserSession?> SignInAsync(CancellationToken ct) => Task.FromResult(Current);
    public Task<UserSession?> RefreshAsync(CancellationToken ct) => GetAsync(ct);
    public Task<UserSession?> GetAsync(CancellationToken ct)
    {
        GetCount++;
        return GetSession?.Invoke(ct) ?? Task.FromResult(Current);
    }

    public Task SignOutAsync(CancellationToken ct)
    {
        Set(null);
        return Task.CompletedTask;
    }

    public static UserSession CreateSession(string subject = "user-one", string company = "company-one")
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "https://identity.example/realms/desktop", sub = subject, company_id = company,
            exp = expiry.ToUnixTimeSeconds()
        });
        var encoded = Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new UserSession($"eyJhbGciOiJSUzI1NiJ9.{encoded}.fixture", null, null, expiry);
    }
}