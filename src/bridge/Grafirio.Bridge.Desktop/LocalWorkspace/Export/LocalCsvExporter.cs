using System.IO;
using System.Text;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Export;

public sealed class LocalCsvExporter(ILogger<LocalCsvExporter> logger) : ILocalCsvExporter
{
    public async Task<bool> ExportAsync(LocalQueryResult result, CancellationToken cancellationToken)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Sorgu sonucunu kaydet", FileName = "grafirio-result.csv",
            Filter = "CSV dosyası (*.csv)|*.csv", DefaultExt = ".csv",
            AddExtension = true, OverwritePrompt = true, CheckPathExists = true
        };
        if (dialog.ShowDialog() != true) return false;
        cancellationToken.ThrowIfCancellationRequested();
        var text = new StringBuilder();
        text.AppendLine(string.Join(",", result.Columns.Select(CsvEncoding.Cell)));
        foreach (var row in result.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            text.AppendLine(string.Join(",", row.Select(CsvEncoding.Cell)));
        }
        // Finish the bounded write even if the view unloads, rather than leave a cancelled partial export.
        await File.WriteAllTextAsync(dialog.FileName, text.ToString(), new UTF8Encoding(true), CancellationToken.None);
        logger.LogInformation("Local CSV exported with {RowCount} rows", result.Rows.Count);
        return true;
    }
}