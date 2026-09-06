using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Storage;

public sealed class DpapiLocalWorkspaceStore(ILogger<DpapiLocalWorkspaceStore> logger) : ILocalWorkspaceStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Grafirio.Desktop.LocalWorkspace.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Grafirio", "Desktop", "local-workspace.dpapi");

    public async Task<WorkspaceDocument> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadDocumentAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<WorkspaceDocument> UpdateAsync(
        Func<WorkspaceDocument, WorkspaceDocument> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = update(await ReadDocumentAsync(cancellationToken));
            WorkspaceValidation.Document(document);
            var plain = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (encrypted.Length > WorkspaceLimits.MaxStoreBytes)
                throw new InvalidOperationException("Yerel depo boyut sınırı aşıldı.");
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporary = _filePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
            File.Move(temporary, _filePath, overwrite: true);
            logger.LogDebug("Local workspace saved with {ConnectionCount} connections", document.Connections.Count);
            return document;
        }
        finally { _gate.Release(); }
    }

    private async Task<WorkspaceDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return new WorkspaceDocument();
        if (new FileInfo(_filePath).Length > WorkspaceLimits.MaxStoreBytes)
            throw new InvalidDataException("Yerel depo boyut sınırı aşıldı.");
        var encrypted = await File.ReadAllBytesAsync(_filePath, cancellationToken);
        var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var document = JsonSerializer.Deserialize<WorkspaceDocument>(plain, JsonOptions)
                ?? throw new InvalidDataException("Yerel depo okunamadı.");
            WorkspaceValidation.Document(document);
            return document;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}