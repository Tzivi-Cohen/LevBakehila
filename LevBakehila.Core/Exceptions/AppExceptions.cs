namespace LevBakehila.Core.Exceptions;

// Business exceptions thrown by the Service layer.
// ExceptionHandlingMiddleware in the API translates each type to an HTTP status code.
public abstract class AppException : Exception
{
    protected AppException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

// 404
public class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message) { }
}

// 400 - a business rule was broken by the request itself
public class BusinessRuleException : AppException
{
    public BusinessRuleException(string message) : base(message) { }
}

// 409 - the request conflicts with the current state of the data
public class ConflictException : AppException
{
    public ConflictException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

// 409 - two users changed the same data at the same moment (RowVersion / unique index)
public class ConcurrencyConflictException : ConflictException
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

// 403
public class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message) { }
}

// 401
public class UnauthorizedException : AppException
{
    public UnauthorizedException(string message) : base(message) { }
}
