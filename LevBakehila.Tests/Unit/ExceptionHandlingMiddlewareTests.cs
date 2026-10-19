using System.Text.Json;
using LevBakehila.API.Middleware;
using LevBakehila.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace LevBakehila.Tests.Unit;

public class ExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _logger = new();

    private async Task<(HttpContext Context, JsonElement Body)> InvokeWith(Exception exception)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        context.Items[CorrelationIdMiddleware.ItemKey] = "test-correlation-id";

        var middleware = new ExceptionHandlingMiddleware(_ => throw exception, _logger.Object);
        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        return (context, body);
    }

    private void VerifyLogged(LogLevel level)
        => _logger.Verify(l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

    [Fact]
    public async Task ConcurrencyConflict_Returns409_AndLogsWarning()
    {
        var (context, body) = await InvokeWith(new ConcurrencyConflictException("conflict"));

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("conflict", body.GetProperty("message").GetString());
        Assert.Equal("test-correlation-id", body.GetProperty("correlationId").GetString());
        VerifyLogged(LogLevel.Warning);
    }

    [Theory]
    [InlineData(typeof(NotFoundException), StatusCodes.Status404NotFound)]
    [InlineData(typeof(BusinessRuleException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(ForbiddenException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(UnauthorizedException), StatusCodes.Status401Unauthorized)]
    public async Task AppExceptions_MapToStatusCodes(Type exceptionType, int expectedStatus)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "message")!;

        var (context, _) = await InvokeWith(exception);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnexpectedException_Returns500_WithoutInternalDetails_AndLogsError()
    {
        var (context, body) = await InvokeWith(new InvalidOperationException("secret internal detail"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("secret internal detail", body.GetRawText());
        VerifyLogged(LogLevel.Error);
    }

    public static TheoryData<Exception> HandledExceptions => new()
    {
        new NotFoundException("message"),
        new BusinessRuleException("message"),
        new ConflictException("message"),
        new ForbiddenException("message"),
        new UnauthorizedException("message")
    };

    [Theory]
    [MemberData(nameof(HandledExceptions))]
    public async Task HandledBusinessExceptions_AreLoggedOnce_AsWarning(Exception exception)
    {
        await InvokeWith(exception);

        VerifyLogged(LogLevel.Warning);
        _logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task ClientCancelledRequest_IsNotA500_AndNotLoggedAsError()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        var middleware = new ExceptionHandlingMiddleware(_ => throw new OperationCanceledException(aborted.Token), _logger.Object);

        await middleware.InvokeAsync(context);

        Assert.NotEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        VerifyLogged(LogLevel.Information);
        _logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }
}
