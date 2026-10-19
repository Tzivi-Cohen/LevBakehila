using System.Text.Json;
using LevBakehila.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace LevBakehila.Tests.Unit;

// Validation errors (400) use the same JSON shape as every other error: statusCode, message, correlationId (+ errors)
public class ErrorResponseTests
{
    [Fact]
    public void InvalidModelState_Returns400_InTheUniformShape_WithFieldErrors()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CorrelationIdMiddleware.ItemKey] = "test-correlation-id";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        actionContext.ModelState.AddModelError("Email", "כתובת האימייל אינה תקינה.");
        actionContext.ModelState.AddModelError("Password", "סיסמה היא שדה חובה.");

        var result = Assert.IsType<BadRequestObjectResult>(ErrorResponse.FromInvalidModelState(actionContext));
        var body = Assert.IsType<ErrorResponse>(result.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, body.StatusCode);
        Assert.Equal(ErrorResponse.ValidationMessage, body.Message);
        Assert.Equal("test-correlation-id", body.CorrelationId);
        Assert.Equal(new[] { "כתובת האימייל אינה תקינה." }, body.Errors!["Email"]);
        Assert.Equal(new[] { "סיסמה היא שדה חובה." }, body.Errors["Password"]);
    }

    [Fact]
    public void NonValidationError_OmitsTheErrorsProperty()
    {
        var json = JsonSerializer.Serialize(new ErrorResponse(409, "conflict", "id"));

        Assert.DoesNotContain("Errors", json);
        Assert.Contains("\"Message\":\"conflict\"", json);
    }
}
