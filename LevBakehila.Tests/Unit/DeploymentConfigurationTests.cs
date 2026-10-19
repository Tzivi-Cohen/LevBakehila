using LevBakehila.API.Configuration;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LevBakehila.Tests.Unit;

// Configuration used when the API runs on Render. Only fake values - no real hosts or passwords.
public class DeploymentConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void ResolveConnectionString_DefaultConnectionWinsOverDatabaseUrl()
    {
        var config = Config(
            ("ConnectionStrings:DefaultConnection", "Host=configured;Database=db1;Username=u"),
            ("DATABASE_URL", "postgresql://other:pw@elsewhere/db2"));

        Assert.Equal("Host=configured;Database=db1;Username=u", DeploymentConfiguration.ResolveConnectionString(config));
    }

    [Fact]
    public void ResolveConnectionString_NothingConfigured_ReturnsNull()
    {
        Assert.Null(DeploymentConfiguration.ResolveConnectionString(Config()));
    }

    [Fact]
    public void ResolveConnectionString_DatabaseUrl_IsConvertedWithSslRequired()
    {
        var config = Config(("DATABASE_URL", "postgresql://app_user:p%40ss%3Aword@db.example.test:6543/app_db"));

        var builder = new NpgsqlConnectionStringBuilder(DeploymentConfiguration.ResolveConnectionString(config));

        Assert.Equal("db.example.test", builder.Host);
        Assert.Equal(6543, builder.Port);
        Assert.Equal("app_db", builder.Database);
        Assert.Equal("app_user", builder.Username);
        Assert.Equal("p@ss:word", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void FromDatabaseUrl_NoPort_Uses5432()
    {
        var builder = new NpgsqlConnectionStringBuilder(DeploymentConfiguration.FromDatabaseUrl("postgres://u:p@internal-host/app_db"));

        Assert.Equal(5432, builder.Port);
        Assert.Equal("internal-host", builder.Host);
    }

    [Fact]
    public void FromDatabaseUrl_SslModeInUrl_IsRespected()
    {
        var builder = new NpgsqlConnectionStringBuilder(DeploymentConfiguration.FromDatabaseUrl("postgres://u:p@h/db?sslmode=verify-full"));

        Assert.Equal(SslMode.VerifyFull, builder.SslMode);
    }

    [Theory]
    [InlineData("mysql://u:p@h/db")]
    [InlineData("not a url")]
    public void FromDatabaseUrl_NotPostgres_ThrowsWithoutEchoingTheUrl(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DeploymentConfiguration.FromDatabaseUrl(url));

        Assert.DoesNotContain(url, ex.Message);
    }

    [Fact]
    public void GetAllowedOrigins_TrimsTrailingSlashAndSkipsEmptyAndDuplicates()
    {
        var config = Config(
            ("Cors:AllowedOrigins:0", "https://client.example.test/"),
            ("Cors:AllowedOrigins:1", " "),
            ("Cors:AllowedOrigins:2", "https://client.example.test"),
            ("Cors:AllowedOrigins:3", "http://localhost:5173"));

        Assert.Equal(new[] { "https://client.example.test", "http://localhost:5173" }, DeploymentConfiguration.GetAllowedOrigins(config));
    }

    [Fact]
    public void GetAllowedOrigins_NoneConfigured_ReturnsEmpty()
    {
        Assert.Empty(DeploymentConfiguration.GetAllowedOrigins(Config()));
    }

    [Fact]
    public void GetPort_ReadsPortOrReturnsNull()
    {
        Assert.Equal(10000, DeploymentConfiguration.GetPort(Config(("PORT", "10000"))));
        Assert.Null(DeploymentConfiguration.GetPort(Config()));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public void GetPort_Invalid_Throws(string value)
    {
        Assert.Throws<InvalidOperationException>(() => DeploymentConfiguration.GetPort(Config(("PORT", value))));
    }
}
