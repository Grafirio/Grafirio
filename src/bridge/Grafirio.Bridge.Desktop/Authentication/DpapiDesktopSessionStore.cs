using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Grafirio.Bridge.Desktop.Authentication;

public sealed class DpapiDesktopSessionStore : IDesktopSessionStore
{
    private const string SessionFileName = "session.dat";
    private readonly DesktopIdentityAuthority _authority;
    private readonly ILogger<DpapiDesktopSessionStore> _logger;
    private readonly string _path;
    private readonly byte[] _entropy;

    public DpapiDesktopSessionStore(
        DesktopIdentityAuthority authority,
        ILogger<DpapiDesktopSessionStore> logger,
        string? directoryPath = null)
    {
        _authority = authority;
        _logger = logger;
        _path = Path.Combine(directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Grafirio", "Desktop"), SessionFileName);
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"Grafirio.Desktop\n{authority.Issuer.AbsoluteUri}\n{authority.ClientId}"));
    }

    public async Task<UserSession?> LoadAsync(CancellationToken ct)
    {
        byte[] encrypted;
        try
        {
            encrypted = await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        byte[]? plaintext = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            plaintext = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<PersistedDesktopSession>(plaintext);
            if (stored is null || !_authority.MatchesIssuer(stored.Issuer)
                || stored.ClientId != _authority.ClientId
                || string.IsNullOrWhiteSpace(stored.Session?.AccessToken))
            {
                _logger.LogWarning("The stored desktop session is invalid or belongs to another identity configuration.");
                return null;
            }
            return stored.Session;
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("The stored desktop session cannot be decrypted for this user and identity configuration.");
            return null;
        }
        catch (JsonException)
        {
            _logger.LogWarning("The stored desktop session has an invalid format.");
            return null;
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task SaveAsync(UserSession session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new PersistedDesktopSession
        {
            Issuer = _authority.Issuer.AbsoluteUri,
            ClientId = _authority.ClientId,
            Session = session
        });
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(plaintext, _entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            }))
            {
                await stream.WriteAsync(encrypted, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public Task DeleteAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            File.Delete(_path);
        }
        catch (DirectoryNotFoundException)
        {
            // A first-run sign-out already has no persisted session.
        }
        return Task.CompletedTask;
    }
}