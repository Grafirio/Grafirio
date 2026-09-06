using System.Net;
using System.Net.Http;
using System.Text;
using Grafirio.Bridge.Desktop.Authentication;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge.Desktop.Tests.Authentication;

internal sealed class AuthenticationTestContext : IDisposable
{
    public const string Issuer = "https://identity.example/realms/grafirio";
    public const string ClientId = "desktop";
    public const string Discovery = """
        {"issuer":"https://identity.example/realms/grafirio",
         "authorization_endpoint":"https://identity.example/authorize",
         "token_endpoint":"https://identity.example/token"}
        """;
    public const string TokenResponse = """
        {"access_token":"new-access","refresh_token":"new-refresh","id_token":"new-id",
         "token_type":"Bearer","expires_in":300}
        """;

    public TestClock Clock { get; } = new();
    public MemoryStore Store { get; } = new();
    public FakeBrowser Browser { get; } = new();
    public StubHandler Handler { get; } = new();
    public HttpClient HttpClient { get; }
    public DesktopOAuthClient OAuth { get; }
    public DesktopSessionManager Manager { get; }

    public AuthenticationTestContext()
    {
        HttpClient = new HttpClient(Handler);
        OAuth = new DesktopOAuthClient(HttpClient, Authority(), Clock);
        Manager = new DesktopSessionManager(Browser, OAuth, Store, Clock);
    }

    public UserSession Session(TimeSpan? lifetime = null) =>
        new("old-access", "old-refresh", "old-id", Clock.GetUtcNow() + (lifetime ?? TimeSpan.FromMinutes(10)));

    public static DesktopIdentityAuthority Authority(string issuer = Issuer, string clientId = ClientId) =>
        new(Options.Create(new BridgeOptions { IdentityUrl = issuer, InstallerClientId = clientId }));

    public static HttpResponseMessage Json(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    public void Dispose()
    {
        Manager.Dispose();
        HttpClient.Dispose();
    }

    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class MemoryStore : IDesktopSessionStore
    {
        public UserSession? Session { get; set; }
        public int Saves { get; private set; }
        public int Deletes { get; private set; }
        public Task<UserSession?> LoadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Session);
        }
        public Task SaveAsync(UserSession session, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Session = session;
            Saves++;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Session = null;
            Deletes++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeBrowser : IBrowserLogin
    {
        public UserSession? Session { get; set; }
        public Task<UserSession?> TryLoginAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Session);
        }
    }

    internal sealed class StubHandler : HttpMessageHandler
    {
        public List<string> TokenRequests { get; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (_, _) => Task.FromResult(Json(TokenResponse));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get) return Json(Discovery);
            TokenRequests.Add(await request.Content!.ReadAsStringAsync(ct));
            return await Respond(request, ct);
        }
    }
}