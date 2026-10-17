using FsCheck.Xunit;
using Grafirio.QueryPolicy;
using Policy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.QueryPolicy.Tests;

public sealed class QueryPolicyProperties
{
    // Arbitrary input becomes a SQL identifier, including reserved words and ] characters.
    // Keep names within SQL Server's 128-character identifier limit, with a safe prefix.
    private static string Identifier(string? value) => "x" + new string((value ?? "")
        .Where(c => c >= ' ' && c <= '~').Take(60).ToArray());

    [Property(MaxTest = 200)]
    public void Quoted_identifiers_round_trip_through_the_real_parser(string? schema, string? name)
    {
        var identity = new SqlTableIdentity(Identifier(schema), Identifier(name));
        Assert.Equal(identity, Policy.ParseTableIdentity(identity.CanonicalName));
    }

    [Property(MaxTest = 200)]
    public void An_allowlist_never_grants_a_different_table(string? name)
    {
        var identity = new SqlTableIdentity("dbo", Identifier(name));
        var sql = $"SELECT 1 FROM {identity.CanonicalName}";
        Assert.Single(Policy.Validate(sql, [identity.CanonicalName]).Tables);
        Assert.Throws<QueryPolicyException>(() => Policy.Validate(sql, ["[dbo].[" + identity.Name.Replace("]", "]]") + "_other]"]));
        Assert.Throws<QueryPolicyException>(() => Policy.Validate(sql));
    }

    [Property(MaxTest = 200)]
    public void A_second_write_statement_is_rejected_for_any_literal(int value)
    {
        var sql = $"SELECT {value}; DELETE FROM [dbo].[Items]";
        Assert.Throws<QueryPolicyException>(() => Policy.Validate(sql, ["dbo.Items"]));
    }

    [Property(MaxTest = 200)]
    public void Sql_punctuation_inside_a_quoted_literal_does_not_become_a_statement(string? value)
    {
        // This deliberately uses a literal to exercise the policy parser's token boundaries.
        var literal = (value ?? "").Replace("\0", "").Replace("'", "''");
        var sql = $"SELECT N'{literal}; DELETE FROM dbo.Items; --' FROM dbo.Items";
        Assert.Single(Policy.Validate(sql, ["dbo.Items"]).Tables);
    }
}
