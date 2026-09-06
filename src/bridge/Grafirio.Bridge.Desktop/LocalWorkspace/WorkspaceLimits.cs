namespace Grafirio.Bridge.Desktop.LocalWorkspace;

public static class WorkspaceLimits
{
    public const int MaxConnections = 100;
    public const int MaxSqlLength = 20_000;
    public const int MaxRows = 5_000;
    public const int MaxColumns = 200;
    public const int MaxResultCharacters = 2_000_000;
    public const int MaxCellCharacters = 16_000;
    public const int MaxTimeoutSeconds = 120;
    public const int ConnectTimeoutSeconds = 15;
    public const int MaxAllowedTables = 500;
    public const int MaxMessageLength = 100_000;
    public const int MaxStoreBytes = 4_000_000;
}