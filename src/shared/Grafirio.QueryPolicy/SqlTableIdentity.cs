namespace Grafirio.QueryPolicy;

/// <summary>Decoded identifiers; comparisons are ordinal to remain safe on case-sensitive databases.</summary>
public sealed record SqlTableIdentity(string? Schema, string Name)
{
    public string CanonicalName => Schema is null ? Quote(Name) : $"{Quote(Schema)}.{Quote(Name)}";

    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}