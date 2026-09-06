using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grafirio.Bridge.Contracts;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge;

/// <summary>
/// Enrolls the machine using the installer's approval, not a machine identity.
/// </summary>
public class BridgeEnrollment(
    IOptions<BridgeOptions> options,
    BridgeState state,
    BridgeDeviceLogin deviceLogin,
    IBridgeDisplay display,
    ILogger<BridgeEnrollment> logger)
{
    private const string EnrollmentFailureMessage = "Bridge kaydı tamamlanamadı. Lütfen bağlantı ayarlarını kontrol edip yeniden deneyin.";
    private readonly BridgeOptions _options = options.Value;
    private readonly Func<HttpClient> _createClient = BridgeEndpointSecurity.CreateClient;

    internal BridgeEnrollment(
        IOptions<BridgeOptions> options, BridgeState state, BridgeDeviceLogin deviceLogin,
        IBridgeDisplay display, ILogger<BridgeEnrollment> logger, Func<HttpClient> createClient)
        : this(options, state, deviceLogin, display, logger)
    {
        _createClient = createClient;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Obtains device-flow approval for unattended service enrollment.
    /// </summary>
    public async Task<bool> TryEnrollAsync(CancellationToken ct)
    {
        if (!TryGetEnrollmentEndpoint(out _)) return false;

        var installerToken = await deviceLogin.TryLoginAsync(ct);

        if (installerToken is null)
        {
            logger.LogError("Enrollment approval was not obtained.");
            display.ShowStatus(
                BridgeStatus.EnrollmentFailed, EnrollmentFailureMessage);
            return false;
        }

        return await EnrollWithTokenAsync(installerToken, ct);
    }

    private bool TryGetEnrollmentEndpoint(out Uri? endpoint)
    {
        try
        {
            endpoint = BridgeEndpointSecurity.GetEnrollmentEndpoint(_options);
            return true;
        }
        catch (InvalidOperationException)
        {
            endpoint = null;
            logger.LogWarning("Bridge enrollment endpoint configuration was rejected.");
            display.ShowStatus(BridgeStatus.EnrollmentFailed, EnrollmentFailureMessage);
            return false;
        }
    }

    /// <summary>
    /// Enrolls using an installer token already obtained by either shell.
    /// </summary>
    public async Task<bool> EnrollWithTokenAsync(string installerToken, CancellationToken ct)
    {
        if (!TryGetEnrollmentEndpoint(out var endpoint)) return false;

        display.ShowStatus(BridgeStatus.Registering);

        logger.LogInformation("Bridge enrollment requested.");

        try
        {
            using var client = _createClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", installerToken);

            using var response = await client.PostAsJsonAsync(endpoint, new
            {
                machineName = Environment.MachineName,
                bridgeVersion = _options.Version,
                protocolVersion = BridgeProtocol.Version,
                name = _options.Name,
            }, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Bridge enrollment rejected ({StatusCode}).", (int)response.StatusCode);
                display.ShowStatus(
                    BridgeStatus.EnrollmentFailed,
                    EnrollmentFailureMessage);
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<EnrollResponse>(body, JsonOptions);

            if (result is null || result.BridgeId == Guid.Empty
                || string.IsNullOrEmpty(result.ClientId)
                || string.IsNullOrEmpty(result.ClientSecret)
                || string.IsNullOrEmpty(result.TokenEndpoint))
            {
                logger.LogError("Bridge enrollment response did not contain valid credentials.");
                display.ShowStatus(
                    BridgeStatus.EnrollmentFailed,
                    EnrollmentFailureMessage);
                return false;
            }

            var tokenEndpoint = BridgeEndpointSecurity.ValidateTokenEndpoint(
                result.TokenEndpoint, _options.IdentityUrl);
            state.SaveEnrollment(
                result.BridgeId,
                result.CompanyId ?? "",
                new BridgeCredentials(result.ClientId, result.ClientSecret, tokenEndpoint.AbsoluteUri));

            logger.LogInformation("Bridge enrollment completed ({BridgeId}).", result.BridgeId);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Exception messages can contain response data or credential-bearing URLs.
            logger.LogError("Bridge enrollment failed ({ErrorType}).", ex.GetType().Name);
            display.ShowStatus(
                BridgeStatus.EnrollmentFailed,
                EnrollmentFailureMessage);

            return false;
        }
    }

    private class EnrollResponse
    {
        public Guid BridgeId { get; set; }
        public string? CompanyId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? TokenEndpoint { get; set; }
    }
}
