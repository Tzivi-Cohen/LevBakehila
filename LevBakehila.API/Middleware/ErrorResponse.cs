using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace LevBakehila.API.Middleware;

// The one JSON shape for every error the API returns (400 validation, 401, 403, 404, 409, 500).
// Errors is filled only for validation errors: field name -> messages.
public record ErrorResponse(
    int StatusCode,
    string Message,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IDictionary<string, string[]>? Errors = null)
{
    public const string ValidationMessage = "הבקשה אינה תקינה.";

    public static ErrorResponse For(HttpContext context, int statusCode, string message, IDictionary<string, string[]>? errors = null)
        => new(statusCode, message, CorrelationIdMiddleware.GetCorrelationId(context), errors);

    // Replaces the default ProblemDetails of [ApiController] when ModelState is invalid
    public static IActionResult FromInvalidModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "ערך לא תקין." : e.ErrorMessage).ToArray());

        return new BadRequestObjectResult(For(context.HttpContext, StatusCodes.Status400BadRequest, ValidationMessage, errors));
    }

    public static Task WriteAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(For(context, statusCode, message));
    }
}
