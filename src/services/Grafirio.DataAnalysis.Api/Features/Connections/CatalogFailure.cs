using MongoDB.Driver;
using Npgsql;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

/// <summary>
/// Identifies which catalog store (PostgreSQL or MongoDB) caused a failure.
/// </summary>
public static class CatalogFailure
{
    public const string PostgresComponent = "postgres";
    public const string MongoComponent = "mongo";

    /// <summary>
    /// Returns the failing component, or null when the exception is not a store outage.
    /// Inner exceptions are inspected because EF Core wraps connection errors in
    /// <c>RetryLimitExceededException</c> once retry-on-failure is enabled.
    /// </summary>
    public static string? FindComponent(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is NpgsqlException) return PostgresComponent;
            if (current is MongoException) return MongoComponent;
        }

        return null;
    }
}
