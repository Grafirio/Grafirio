using System.Net;
using System.Net.Http;
using System.Text.Json;
using Grafirio.Bridge.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.Bridge.Tests;

// All HTTP responses are supplied in memory; a production transport is never opened.
internal sealed class BridgeSecurityTestContext : IDisposable
{
    public const string IdentityUrl = "https://identity.example/realms/grafirio";
    public const string TokenEndpoint = IdentityUrl + "/protocol/openid-connect/token";
    public const string Secret = "machine-secret-canary";
    public const string InstallerToken = "installer-token-canary";
    public const string AccessToken = "access-token-canary";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public BridgeOptions Options { get; } = new()
    {
        ServerUrl = "https://cloud.example",
        IdentityUrl = IdentityUrl
    };
    public BridgeState State { get; }
    public RecordingDisplay Display { get; } = new();
    public List<string> Logs { get; } = [];
    public int ClientCreations { get; private set; }
    public int RequestCount { get; private set; }
    public Uri? RequestUri { get; private set; }
    public string? Authorization { get; private set; }
    public string? RequestBody { get; private set; }
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "{}";
    public string? Location { get; set; }
    public Exception? RequestFailure { get; set; }

    public BridgeSecurityTestContext()
    {
        State = new BridgeState(NullLogger<BridgeState>.Instance, Path.Combine(_directory, "state.dat"));
    }

    public BridgeEnrollment CreateEnrollment()
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var login = new BridgeDeviceLogin(options, Display, NullLogger<BridgeDeviceLogin>.Instance);
        return new BridgeEnrollment(options, State, login, Display,
            new RecordingLogger<BridgeEnrollment>(Logs), CreateClient);
    }

    public BridgeTokenSource CreateTokenSource() => new(State,
        new RecordingLogger<BridgeTokenSource>(Logs),
        Microsoft.Extensions.Options.Options.Create(Options), CreateClient);

    public void StoreCredentials(string endpoint = TokenEndpoint) =>
        State.SaveEnrollment(Guid.NewGuid(), "company", new BridgeCredentials("machine-client", Secret, endpoint));

    public void SetEnrollmentResponse(string endpoint = TokenEndpoint) =>
        ResponseBody = JsonSerializer.Serialize(new
        {
            bridgeId = Guid.NewGuid(), companyId = "company",
            clientId = "machine-client", clientSecret = Secret, tokenEndpoint = endpoint
        });

    public void AssertNoSecretsExposed()
    {
        var output = string.Join('\n', Logs.Concat(Display.Details));
        Assert.DoesNotContain(Secret, output);
        Assert.DoesNotContain(InstallerToken, output);
        Assert.DoesNotContain(AccessToken, output);
    }

    private HttpClient CreateClient()
    {
        ClientCreations++;
        return new HttpClient(new StubHandler(this));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    internal sealed class RecordingDisplay : IBridgeDisplay
    {
        public List<string> Details { get; } = [];
        public List<BridgeStatus> Statuses { get; } = [];
        public void ShowDeviceCode(DeviceCodePrompt prompt) => Assert.Fail("Device login must not run in these tests.");
        public void ShowStatus(BridgeStatus status, string? detail = null)
        {
            Statuses.Add(status);
            if (detail is not null) Details.Add(detail);
        }
    }

    private sealed class StubHandler(BridgeSecurityTestContext context) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            context.RequestCount++;
            context.RequestUri = request.RequestUri;
            context.Authorization = request.Headers.Authorization?.ToString();
            context.RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (context.RequestFailure is not null) throw context.RequestFailure;
            var response = new HttpResponseMessage(context.StatusCode)
            {
                Content = new StringContent(context.ResponseBody)
            };
            if (context.Location is not null) response.Headers.Location = new Uri(context.Location);
            return response;
        }
    }

    private sealed class RecordingLogger<T>(List<string> messages) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
            if (exception is not null) messages.Add(exception.ToString());
        }
    }
}