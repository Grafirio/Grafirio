using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grafirio.Bridge.Desktop.Cloud;

internal sealed class DesktopBridgeHost(
    IHost host, BridgeState state, BridgeEnrollment enrollment,
    BridgeWorker worker, IHostApplicationLifetime lifetime,
    DesktopBridgeDisplay display) : IAsyncDisposable
{
    private const string ConnectionFailureMessage = "Bulut bağlantısı kurulamadı. Yerel çalışma alanını kullanmaya devam edebilirsiniz.";

    public static DesktopBridgeHost Create(
        BridgeOptions options, ILoggerFactory loggerFactory, DesktopBridgeDisplay display)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(loggerFactory);
        builder.Services.AddSingleton<IOptions<BridgeOptions>>(Options.Create(options));
        builder.Services.AddSingleton<IBridgeDisplay>(display);
        builder.Services.AddBridgeCore(runInBackground: true);
        builder.Services.Replace(ServiceDescriptor.Singleton(provider => new BridgeState(
            provider.GetRequiredService<ILogger<BridgeState>>(),
            options.StatePath, DataProtectionScope.CurrentUser)));

        var host = builder.Build();
        try
        {
            return new DesktopBridgeHost(host,
                host.Services.GetRequiredService<BridgeState>(),
                host.Services.GetRequiredService<BridgeEnrollment>(),
                host.Services.GetRequiredService<BridgeWorker>(),
                host.Services.GetRequiredService<IHostApplicationLifetime>(), display);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public async Task StartAsync(UserSession session, CancellationToken cancellationToken)
    {
        state.Load();
        cancellationToken.ThrowIfCancellationRequested();
        if (!state.IsEnrolled)
        {
            var enrolled = await enrollment.EnrollWithTokenAsync(session.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!enrolled || !state.IsEnrolled)
                throw new DesktopBridgeException(ConnectionFailureMessage);
        }

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lifetime.ApplicationStopping);
        var attempt = display.ConnectionAttempt.WaitAsync(startupCancellation.Token);
        var completed = await Task.WhenAny(attempt, worker.ExecuteTask!).ConfigureAwait(false);
        if (completed != attempt || !await attempt.ConfigureAwait(false))
            throw new DesktopBridgeException(ConnectionFailureMessage);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Once shutdown starts, complete it before another identity can receive queries.
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (host is IAsyncDisposable disposable)
                await disposable.DisposeAsync().ConfigureAwait(false);
            else
                host.Dispose();
        }
    }
}