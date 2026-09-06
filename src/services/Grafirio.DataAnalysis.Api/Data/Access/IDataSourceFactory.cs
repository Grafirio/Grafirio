using Grafirio.DataAnalysis.Api.Data.Entities;

namespace Grafirio.DataAnalysis.Api.Data.Access;

public interface IDataSourceFactory
{
    Task<IDataSourceSession> OpenAsync(DataSourceTarget target, CancellationToken ct = default);
    Task<IDataSourceSession> OpenAsync(SavedConnection connection, CancellationToken ct = default);
    Task<ProbeResult> ProbeAsync(SavedConnection connection, CancellationToken ct = default);
}
