using System.Data.Common;
using System.Diagnostics;
using Grafirio.Bridge.Contracts;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Data;

public static class LocalResultReader
{
    public static async Task<LocalQueryResult> ReadAsync(
        DbCommand command, int maxRows, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        if (reader.FieldCount > WorkspaceLimits.MaxColumns)
            throw new InvalidOperationException("Sonuç çok fazla sütun içeriyor.");
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<string?[]>();
        var characters = columns.Sum(column => column.Length);
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= maxRows) { truncated = true; break; }
            var row = new string?[reader.FieldCount];
            for (var index = 0; index < row.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await reader.IsDBNullAsync(index, cancellationToken)) continue;
                var type = reader.GetFieldType(index);
                if (type == typeof(string))
                {
                    using var text = reader.GetTextReader(index);
                    var buffer = new char[WorkspaceLimits.MaxCellCharacters + 1];
                    var count = await text.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
                    if (count > WorkspaceLimits.MaxCellCharacters)
                        throw new InvalidOperationException("Hücre boyutu sınırı aşıldı; SQL ile daha küçük bir değer seçin.");
                    row[index] = new string(buffer, 0, count);
                }
                else if (type == typeof(byte[]))
                {
                    var buffer = new byte[WorkspaceLimits.MaxCellCharacters / 2 + 1];
                    var count = reader.GetBytes(index, 0, buffer, 0, buffer.Length);
                    if (count == buffer.Length)
                        throw new InvalidOperationException("İkili hücre boyutu sınırı aşıldı.");
                    row[index] = Convert.ToHexString(buffer.AsSpan(0, (int)count));
                }
                else row[index] = SqlValueCodec.Encode(reader.GetValue(index));
                characters += row[index]?.Length ?? 0;
            }
            if (characters > WorkspaceLimits.MaxResultCharacters) { truncated = true; break; }
            rows.Add(row);
        }
        return new LocalQueryResult(columns, rows, truncated, stopwatch.ElapsedMilliseconds);
    }
}