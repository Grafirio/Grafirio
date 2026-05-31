namespace Grafirio.Commerce.Api;

public record MongoOption
{
    public string ConnectionString { get; init; } = default!;
}
