using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

public sealed class BridgeInstallerTests
{
    private const string SetupFileName = "GrafirioSetup.exe";
    private const string SetupUrl = "https://installers.example.com/GrafirioSetup.exe";

    [Fact]
    public void UnconfiguredInstallerIsUnavailableAndReturnsNotFound()
    {
        var installer = CreateInstaller();

        var info = installer.Describe();

        Assert.False(info.Available);
        Assert.Equal(SetupFileName, info.FileName);
        Assert.Null(info.Url);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(installer.Serve()).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankConfigurationIsUnavailable(string? value)
    {
        var installer = CreateInstaller(value, value);

        Assert.False(installer.Describe().Available);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(installer.Serve()).StatusCode);
    }

    [Fact]
    public void ConfiguredUrlTakesPrecedenceAndRedirectsToRealSetup()
    {
        var installer = CreateInstaller($"  {SetupUrl}  ", "missing-local-file.exe");

        var info = installer.Describe();
        var response = Assert.IsType<RedirectHttpResult>(installer.Serve());

        Assert.True(info.Available);
        Assert.Equal(SetupFileName, info.FileName);
        Assert.Equal(SetupUrl, info.Url);
        Assert.Null(info.SizeBytes);
        Assert.Null(info.PublishedAt);
        Assert.Equal(SetupUrl, response.Url);
        Assert.False(response.Permanent);
    }

    [Fact]
    public void MissingLocalInstallerReturnsNotFound()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), SetupFileName);
        var installer = CreateInstaller(path: missingPath);

        Assert.False(installer.Describe().Available);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(installer.Serve()).StatusCode);
    }

    [Fact]
    public async Task LocalInstallerProvidesSetupNameMetadataAndRangeSupportAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
        byte[] content = [0x4d, 0x5a, 0x00, 0x01];
        await File.WriteAllBytesAsync(path, content);
        try
        {
            var installer = CreateInstaller(path: $" {path} ");
            var info = installer.Describe();

            Assert.True(info.Available);
            Assert.Equal(SetupFileName, info.FileName);
            Assert.Equal(content.LongLength, info.SizeBytes);
            Assert.Equal(File.GetLastWriteTimeUtc(path), info.PublishedAt);
            Assert.Null(info.Url);

            var response = Assert.IsType<FileStreamHttpResult>(installer.Serve());
            await using var stream = response.FileStream;
            Assert.Equal(SetupFileName, response.FileDownloadName);
            Assert.Equal("application/octet-stream", response.ContentType);
            Assert.True(response.EnableRangeProcessing);
            using var downloaded = new MemoryStream();
            await stream.CopyToAsync(downloaded);
            Assert.Equal(content, downloaded.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static BridgeInstaller CreateInstaller(string? url = null, string? path = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BridgeInstaller:Url"] = url,
                ["BridgeInstaller:Path"] = path
            })
            .Build();
        return new BridgeInstaller(configuration, NullLogger<BridgeInstaller>.Instance);
    }
}