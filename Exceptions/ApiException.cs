namespace ZenCloud.Exceptions;

/// <summary>
/// Excepción base para errores de la API
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public object? Details { get; }

    public ApiException(string message, int statusCode = 500, string? errorCode = null, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }
}

/// <summary>
/// Excepción para recursos no encontrados (404)
/// </summary>
public class NotFoundException : ApiException
{
    public NotFoundException(string message, string? errorCode = "RESOURCE_NOT_FOUND", object? details = null)
        : base(message, 404, errorCode, details)
    {
    }
}

/// <summary>
/// Excepción para solicitudes inválidas (400)
/// </summary>
public class BadRequestException : ApiException
{
    public BadRequestException(string message, string? errorCode = "BAD_REQUEST", object? details = null)
        : base(message, 400, errorCode, details)
    {
    }
}

/// <summary>
/// Excepción para acceso no autorizado (401)
/// </summary>
public class UnauthorizedException : ApiException
{
    public UnauthorizedException(string message, string? errorCode = "UNAUTHORIZED", object? details = null)
        : base(message, 401, errorCode, details)
    {
    }
}

/// <summary>
/// Excepción para acceso prohibido (403)
/// </summary>
public class ForbiddenException : ApiException
{
    public ForbiddenException(string message, string? errorCode = "FORBIDDEN", object? details = null)
        : base(message, 403, errorCode, details)
    {
    }
}

/// <summary>
/// Excepción para conflictos de negocio (409)
/// </summary>
public class ConflictException : ApiException
{
    public ConflictException(string message, string? errorCode = "CONFLICT", object? details = null)
        : base(message, 409, errorCode, details)
    {
    }
}

/// <summary>
/// Excepción para errores de validación (422)
/// </summary>
public class ValidationException : ApiException
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationException(Dictionary<string, string[]> errors, string? errorCode = "VALIDATION_ERROR")
        : base("Uno o más errores de validación ocurrieron", 422, errorCode, errors)
    {
        Errors = errors;
    }
}

