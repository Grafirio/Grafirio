namespace Grafirio.DataAnalysis.Api.Models;

public record TestConnectionResponse(
    bool Success,
    string Message,
    string? ConnectionId = null
);

public record TableInfo(
    string TableName,
    string Schema,
    int RowCount
);

public record ColumnInfo(
    string ColumnName,
    string DataType,
    bool IsNullable,
    int? MaxLength
);

public record TableSchemaResponse(
    string TableName,
    string Schema,
    List<ColumnInfo> Columns,
    int RowCount
);
