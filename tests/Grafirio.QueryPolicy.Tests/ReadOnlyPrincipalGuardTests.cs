using System.Data;
using System.Data.Common;
using System.Reflection;
using Grafirio.QueryPolicy;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Grafirio.QueryPolicy.Tests;

public class ReadOnlyPrincipalGuardTests
{
    [Theory]
    [InlineData("PrincipalVerificationSql")]
    [InlineData("PhysicalTableVerificationSql")]
    public void VerificationSqlIsAValidSingleSelect(string fieldName)
    {
        var field = typeof(ReadOnlyPrincipalGuard).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var sql = Assert.IsType<string>(field.GetRawConstantValue());
        var script = Assert.IsType<TSqlScript>(new TSql170Parser(true).Parse(new StringReader(sql), out var errors));
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(Assert.Single(script.Batches).Statements));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData(2)]
    public async Task UnverifiedPrincipalIsBlocked(object? verification)
    {
        await using var connection = new VerificationConnection(verification);
        var exception = await Assert.ThrowsAsync<QueryPolicyException>(() =>
            ReadOnlyPrincipalGuard.VerifyAsync(connection));
        Assert.Contains("blocked", exception.Message);
    }

    [Fact]
    public async Task EffectivePermissionsAndWriterMembershipAreBothChecked()
    {
        await using var connection = new VerificationConnection(1);
        await ReadOnlyPrincipalGuard.VerifyAsync(connection);
        var sql = connection.Command.CommandText;
        Assert.Contains("IS_ROLEMEMBER('db_datawriter')", sql);
        Assert.Contains("IS_ROLEMEMBER('db_owner')", sql);
        Assert.Contains("IS_SRVROLEMEMBER('sysadmin')", sql);
        Assert.Contains("sys.fn_my_permissions", sql);
        Assert.Contains("sys.database_permissions", sql);
        Assert.Contains("sys.server_permissions", sql);
        Assert.Contains("'UPDATE', c.name, 'COLUMN'", sql);
        Assert.DoesNotContain("IS_ROLEMEMBER('db_datareader')", sql);
    }

    [Fact]
    public async Task VerificationErrorsDoNotBecomeSuccess()
    {
        await using var connection = new VerificationConnection(new VerificationException());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReadOnlyPrincipalGuard.VerifyAsync(connection));
        Assert.IsType<VerificationException>(exception.InnerException);
        Assert.Contains("blocked", exception.Message);
    }

    private sealed class VerificationException : DbException;

    private sealed class VerificationConnection(object? result) : DbConnection
    {
        internal VerificationCommand Command { get; } = new(result);
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => Command;
    }

    private sealed class VerificationCommand(object? result) : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => result is Exception exception ? throw exception : result;
        public override void Prepare() => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }
}