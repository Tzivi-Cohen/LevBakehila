using System.Security.Claims;
using LevBakehila.Core.Exceptions;

namespace LevBakehila.API.Middleware;

// First in the pipeline, so it catches exceptions from everything that runs after it.
// Returns one uniform JSON shape (ErrorResponse) and never exposes stack traces to the client.
// This is the single place where a rejected request is logged - the services only roll back and throw.
// Levels (teacher, part D): Error = failure (unexpected exception, 500); Warning = handled exceptional case
// (business rule, not found, forbidden, concurrency conflict); Information = the client cancelled the request.
public class ExceptionHandlingMiddleware
{
    private const string GenericErrorMessage = "אירעה שגיאה בלתי צפויה. נסי שוב מאוחר יותר.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client closed the connection - the CancellationToken stopped the work. Not a server failure,
            // and there is nobody left to send a response to.
            _logger.LogInformation("Request {Method} {Path} was cancelled by the client", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        string correlationId = CorrelationIdMiddleware.GetCorrelationId(context);

        // Derived types first (ConcurrencyConflictException : ConflictException)
        var (statusCode, message) = exception switch
        {
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, exception.Message),
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            BusinessRuleException => (StatusCodes.Status400BadRequest, exception.Message),
            ForbiddenException => (StatusCodes.Status403Forbidden, exception.Message),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, GenericErrorMessage)
        };

        using (_logger.BeginScope(new Dictionary<string, object> { [CorrelationIdMiddleware.ItemKey] = correlationId }))
        {
            if (statusCode == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else if (exception is ConcurrencyConflictException)
            {
                // The user id is a technical key, not personal data
                string userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
                _logger.LogWarning(exception, "Concurrency conflict for {Method} {Path} by user {UserId}: {Message}",
                    context.Request.Method, context.Request.Path, userId, exception.Message);
            }
            else
            {
                _logger.LogWarning("Request {Method} {Path} rejected with {StatusCode}: {Message}",
                    context.Request.Method, context.Request.Path, statusCode, exception.Message);
            }
        }

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        await ErrorResponse.WriteAsync(context, statusCode, message);
    }
}
