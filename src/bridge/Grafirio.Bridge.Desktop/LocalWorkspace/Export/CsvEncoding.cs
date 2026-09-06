namespace Grafirio.Bridge.Desktop.LocalWorkspace.Export;

public static class CsvEncoding
{
    public static string Cell(string? value)
    {
        value ??= "";
        var significant = value.AsSpan().TrimStart();
        // Quoting alone does not prevent spreadsheet formula evaluation.
        if ((!significant.IsEmpty && "=+-@".Contains(significant[0])) ||
            value.Any(character => character is '\t' or '\r' or '\n'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}