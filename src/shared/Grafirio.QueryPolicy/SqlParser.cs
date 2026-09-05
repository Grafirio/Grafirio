using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Grafirio.QueryPolicy;

internal static class SqlParser
{
    private const int MaximumSqlLength = 100_000;

    internal static SelectStatement Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > MaximumSqlLength)
            throw new QueryPolicyException("SQL is empty or exceeds the policy size limit.");

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var normalized = NormalizeListParameters(parser, sql);
        var fragment = parser.Parse(new StringReader(normalized), out var errors);
        if (errors.Count != 0 || fragment is not TSqlScript { Batches.Count: 1 } script ||
            script.Batches[0].Statements.Count != 1 ||
            script.Batches[0].Statements[0] is not SelectStatement select)
            throw new QueryPolicyException("Only one syntactically valid SELECT statement is allowed.");

        return select;
    }

    // Dapper expands IN @Names before execution. Only that exact token pair is parenthesized
    // for parsing; comments, literals and all remaining SQL still pass through the AST policy.
    private static string NormalizeListParameters(TSqlParser parser, string sql)
    {
        var tokens = parser.GetTokenStream(new StringReader(sql), out var errors);
        if (errors.Count != 0 || tokens.Any(token => token.TokenType == TSqlTokenType.Go))
            throw new QueryPolicyException("SQL tokenization failed.");

        var significant = tokens.Where(token => token.TokenType is not
            (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or
             TSqlTokenType.MultilineComment or TSqlTokenType.EndOfFile)).ToList();
        var result = new StringBuilder(sql);
        for (var index = significant.Count - 1; index > 0; index--)
        {
            var token = significant[index];
            if (token.TokenType != TSqlTokenType.Variable ||
                significant[index - 1].TokenType != TSqlTokenType.In)
                continue;

            result.Insert(token.Offset + token.Text.Length, ")");
            result.Insert(token.Offset, "(");
        }

        return result.ToString();
    }
}