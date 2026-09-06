using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Export;

public interface ILocalCsvExporter
{
    Task<bool> ExportAsync(LocalQueryResult result, CancellationToken cancellationToken);
}