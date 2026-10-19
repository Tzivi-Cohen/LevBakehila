using System.Diagnostics;

namespace LevBakehila.API.Middleware;

// Gives every request a CorrelationId, puts it in the logging scope (so NLog writes it on every line)
// and returns it in the X-Correlation-ID response header.
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public static string GetCorrelationId(HttpContext context)
        => context.Items[ItemKey] as string ?? context.TraceIdentifier;

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = Guid.NewGuid().ToString();
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            // Only method + path - never bodies, tokens or passwords
            _logger.LogInformation("Incoming request {Method} {Path}", context.Request.Method, context.Request.Path);
            var stopwatch = Stopwatch.StartNew();

            await _next(context);

            _logger.LogInformation("Completed {Method} {Path} with {StatusCode} in {ElapsedMs} ms",
                context.Request.Method, context.Request.Path, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
    }
}
