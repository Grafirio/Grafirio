using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Features.Analysis;
using Grafirio.QueryPolicy;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class RelationshipQueryPolicyTests
{
    private const string ParentFilter = "WHERE OBJECT_SCHEMA_NAME(fk.parent_object_id) + '.' + tp.name IN @Names";
    private const string ReferencedFilter = "AND OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + tr.name IN @Names";

    [Fact]
    public async Task ActualRelationshipQueryBindsBothEndsAndUsesOnlyApprovedMetadataTables()
    {
        var (sql, parameters, payload) = await CaptureQueryAsync();
        var normalized = Regex.Replace(sql, @"\s+", " ");
        Assert.Contains(ParentFilter, normalized);
        Assert.Contains(ReferencedFilter, normalized);
        Assert.Equal(2, Regex.Matches(sql, "@Names").Count);
        Assert.NotNull(parameters);
        var property = Assert.Single(parameters.GetType().GetProperties());
        Assert.Equal("Names", property.Name);
        var names = Assert.IsAssignableFrom<IEnumerable<string>>(property.GetValue(parameters));
        Assert.Equal([OwnedAnalysisEndpointHarness.OrdersTable, OwnedAnalysisEndpointHarness.CustomersTable], names);
        Assert.DoesNotContain(OwnedAnalysisEndpointHarness.OrdersTable, sql);
        Assert.DoesNotContain(OwnedAnalysisEndpointHarness.CustomersTable, sql);

        var validated = ReadOnlySqlPolicy.Validate(sql, allowMetadata: true);
        SqlTableIdentity[] expectedTables = [new("sys", "columns"), new("sys", "foreign_key_columns"),
            new("sys", "foreign_keys"), new("sys", "tables")];
        Assert.Equal(expectedTables, validated.Tables.OrderBy(table => table.Name, StringComparer.Ordinal));
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(sql));
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(sql,
            validated.Tables.Select(table => table.CanonicalName)));

        Assert.True(payload["success"]!.GetValue<bool>());
        Assert.Equal(1, payload["count"]!.GetValue<int>());
        var relationship = Assert.Single(payload["data"]!.AsArray())!;
        Assert.Equal(OwnedAnalysisEndpointHarness.OrdersTable, relationship["ParentTable"]!.GetValue<string>());
        Assert.Equal(OwnedAnalysisEndpointHarness.CustomersTable, relationship["ReferencedTable"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("sys.sql_logins")]
    [InlineData("sys.dm_exec_sessions")]
    [InlineData("sys.procedures")]
    [InlineData("INFORMATION_SCHEMA.ROUTINES")]
    [InlineData("dbo.Secret")]
    [InlineData("OtherDatabase.sys.foreign_keys")]
    public async Task RelationshipQueryCannotExpandItsMetadataAccess(string unauthorizedTable)
    {
        var (sql, _, _) = await CaptureQueryAsync();
        var expanded = sql + $" AND EXISTS (SELECT 1 FROM {unauthorizedTable})";

        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(expanded, allowMetadata: true));
        if (unauthorizedTable.StartsWith("sys.", StringComparison.Ordinal)
            || unauthorizedTable.StartsWith("INFORMATION_SCHEMA.", StringComparison.Ordinal))
            Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(expanded, [unauthorizedTable], true));
    }

    private static async Task<(string Sql, object? Parameters, JsonNode Payload)> CaptureQueryAsync()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        await harness.Db.SaveChangesAsync();
        var session = new Mock<IDataSourceSession>(MockBehavior.Strict);
        var factory = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        factory.Setup(value => value.OpenAsync(harness.Connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session.Object);
        session.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        string? capturedSql = null;
        object? capturedParameters = null;
        IReadOnlyList<QueryRow> rows =
        [
            Relationship("dbo", "Orders", "dbo", "Customers"),
            Relationship("archive", "Orders", "dbo", "Customers"),
            Relationship("dbo", "Orders", "archive", "Customers"),
            Relationship("dbo", "OrdersArchive", "dbo", "Customers"),
            Relationship("dbo", "Orders", "dbo", "CustomersArchive")
        ];
        session.Setup(value => value.QueryRowsAsync(It.IsAny<string>(), It.IsAny<object?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, int?, CancellationToken>((sql, parameters, _, _) =>
            {
                capturedSql = sql;
                capturedParameters = parameters;
            })
            .ReturnsAsync(rows);
        var method = typeof(AnalysisEndpoints).GetMethod("GetRelationships", BindingFlags.Static | BindingFlags.NonPublic)!;

        var result = await (Task<IResult>)method.Invoke(null,
            [harness.Connection.Id, new AnalysisRequest(harness.SelectedTables.ToList()), harness.Db,
                harness.Identity.Object, factory.Object, harness.Profiles, CancellationToken.None])!;

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var payload = JsonSerializer.SerializeToNode(((IValueHttpResult)result).Value)!;
        Assert.True(payload["success"]!.GetValue<bool>(), payload.ToJsonString());
        Assert.NotNull(capturedSql);
        harness.VerifySelectionRead();
        factory.Verify(value => value.OpenAsync(harness.Connection, It.IsAny<CancellationToken>()), Times.Once);
        factory.VerifyNoOtherCalls();
        session.Verify(value => value.QueryRowsAsync(It.IsAny<string>(), It.IsAny<object?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        session.Verify(value => value.DisposeAsync(), Times.Once);
        session.VerifyNoOtherCalls();
        return (capturedSql, capturedParameters, payload);
    }

    private static QueryRow Relationship(string parentSchema, string parentTable, string referencedSchema,
        string referencedTable) => new(FakeDataSourceSession.Row(
            ("ConstraintName", "FK_Orders_Customers"), ("ParentSchema", parentSchema), ("ParentTable", parentTable),
            ("ParentColumn", "CustomerId"), ("ReferencedSchema", referencedSchema),
            ("ReferencedTable", referencedTable), ("ReferencedColumn", "Id")));
}