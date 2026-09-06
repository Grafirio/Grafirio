using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grafirio.Bridge.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.Bridge.Tests;

public sealed class BridgeStateProtectionTests : IDisposable
{
    private const string ClientSecret = "test-device-secret";
    private const string CompanyId = "test-company";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grafirio-cloud-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultScopeRemainsLocalMachine()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(_directory, "machine.dat");
        var state = new BridgeState(NullLogger<BridgeState>.Instance, path);
        Enroll(state);
        using var document = JsonDocument.Parse(ProtectedData.Unprotect(
            File.ReadAllBytes(path), null, DataProtectionScope.LocalMachine));
        Assert.Equal(ClientSecret, document.RootElement.GetProperty("clientSecret").GetString());
    }

    [Fact]
    public void DesktopScopeRoundTripsWithCurrentUserWithoutPlaintextCredentials()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(_directory, "user.dat");
        var state = new BridgeState(NullLogger<BridgeState>.Instance, path, DataProtectionScope.CurrentUser);
        Enroll(state);
        var protectedBytes = File.ReadAllBytes(path);
        Assert.DoesNotContain(ClientSecret, Encoding.UTF8.GetString(protectedBytes));
        using var document = JsonDocument.Parse(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        Assert.Equal(ClientSecret, document.RootElement.GetProperty("clientSecret").GetString());
        var loaded = new BridgeState(NullLogger<BridgeState>.Instance, path, DataProtectionScope.CurrentUser);
        loaded.Load();
        Assert.True(loaded.IsEnrolled);
        Assert.Equal(state.BridgeId, loaded.BridgeId);
        Assert.Equal(ClientSecret, loaded.Credentials!.ClientSecret);
    }

    private static void Enroll(BridgeState state) => state.SaveEnrollment(Guid.NewGuid(), CompanyId,
        new BridgeCredentials("test-client", ClientSecret, "https://identity.example/token"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}