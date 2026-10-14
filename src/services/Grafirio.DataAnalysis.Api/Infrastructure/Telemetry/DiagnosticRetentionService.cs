using Grafirio.DataAnalysis.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

public sealed class DiagnosticRetentionService(IServiceScopeFactory scopes, ILogger<DiagnosticRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();
                var settings = await db.DiagnosticSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, stoppingToken);
                var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(settings?.RetentionDays ?? 90, 7, 365));
                // Saved test cases are independent snapshots and survive diagnostic cleanup.
                var count = await db.QueryDiagnostics.Where(d => d.CreatedAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                await db.DiagnosticAdminEvents.Where(d => d.CreatedAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                logger.LogInformation("Diagnostic retention removed {Count} records", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Diagnostic retention failed; will retry at next interval"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
