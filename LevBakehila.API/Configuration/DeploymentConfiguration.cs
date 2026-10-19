using Npgsql;

namespace LevBakehila.API.Configuration;

// Settings that change between a local run and a hosted deployment (Render).
// Every value comes from configuration / environment variables - nothing secret is stored here.
public static class DeploymentConfiguration
{
    public const string DatabaseUrlKey = "DATABASE_URL";
    public const string PortKey = "PORT";

    // ConnectionStrings:DefaultConnection (User Secrets locally, ConnectionStrings__DefaultConnection on Render) wins.
    // Otherwise DATABASE_URL in URI form (postgresql://user:pass@host:port/db), as Render shows it, is converted
    // to the key=value form Npgsql expects. SSL is required unless the URL itself sets sslmode.
    public static string? ResolveConnectionString(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string? databaseUrl = configuration[DatabaseUrlKey];
        return string.IsNullOrWhiteSpace(databaseUrl) ? null : FromDatabaseUrl(databaseUrl);
    }

    public static string FromDatabaseUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            // The URL itself is not echoed - it contains the password
            throw new InvalidOperationException($"{DatabaseUrlKey} must be a postgres:// or postgresql:// URL.");
        }

        string[] userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Require
        };

        string? sslMode = GetQueryValue(uri.Query, "sslmode");
        if (sslMode != null && Enum.TryParse<SslMode>(sslMode.Replace("-", ""), ignoreCase: true, out var parsed))
        {
            builder.SslMode = parsed;
        }

        return builder.ConnectionString;
    }

    // Origins come from Cors:AllowedOrigins (on Render: Cors__AllowedOrigins__0, Cors__AllowedOrigins__1, ...).
    // A browser sends the origin without a trailing slash, so one copied with "/" would never match.
    public static string[] GetAllowedOrigins(IConfiguration configuration)
        => (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    // Render tells the container which port to listen on through PORT
    public static int? GetPort(IConfiguration configuration)
    {
        string? value = configuration[PortKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out int port) && port is > 0 and <= 65535
            ? port
            : throw new InvalidOperationException($"{PortKey} must be a port number between 1 and 65535.");
    }

    private static string? GetQueryValue(string query, string key)
        => query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault();
}
