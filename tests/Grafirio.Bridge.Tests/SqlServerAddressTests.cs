using Grafirio.Bridge.Contracts;

namespace Grafirio.Bridge.Tests;

public sealed class SqlServerAddressTests
{
    [Theory]
    [InlineData("localhost", 1433, "tcp:localhost,1433")]
    [InlineData("localhost:1444", 0, "tcp:localhost,1444")]
    [InlineData("[::1]:1444", 1444, "tcp:[::1],1444")]
    [InlineData("::1", 1433, "tcp:[::1],1433")]
    [InlineData("[::ffff:192.168.1.7]:1433", 1433, "tcp:[::ffff:192.168.1.7],1433")]
    [InlineData("sql.example,1444", 1444, "tcp:sql.example,1444")]
    public void NormalizesAddressWithoutChangingHost(string host, int port, string expected) =>
        Assert.Equal(expected, SqlServerAddress.Parse(host, port).DataSource);

    [Theory]
    [InlineData("localhost:1444", 1433)]
    [InlineData("localhost", -1)]
    [InlineData("localhost", 65536)]
    [InlineData("https://localhost", 1433)]
    [InlineData("localhost;Password=secret", 1433)]
    [InlineData("localhost\\SQLEXPRESS", 1433)]
    [InlineData("[invalid]", 1433)]
    [InlineData("[1234]:1433", 1433)]
    [InlineData("[1234]", 1433)]
    [InlineData("1::2::3", 1433)]
    public void RejectsInvalidEndpoints(string host, int port) =>
        Assert.Throws<ArgumentException>(() => SqlServerAddress.Parse(host, port));
}
