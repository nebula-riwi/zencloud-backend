using System.Net;
using System.Text.Json;
using ZenCloud.Exceptions;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ZenCloud.Middleware;

/// <summary>
/// Middleware global para manejo centralizado de excepciones
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var errorLogRepository = serviceProvider.GetRequiredService<IRepository<ErrorLog>>();
            await HandleExceptionAsync(context, ex, errorLogRepository);
        }
    }

    private async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        IRepository<ErrorLog> errorLogRepository)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            Timestamp = DateTime.UtcNow,
            Path = context.Request.Path,
            Method = context.Request.Method
        };

        // Manejar excepciones conocidas de la API
        // ValidationException debe ir antes de ApiException porque hereda de ella
        switch (exception)
        {
            case ValidationException validationException:
                response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                errorResponse.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                errorResponse.Message = validationException.Message;
                errorResponse.ErrorCode = validationException.ErrorCode;
                errorResponse.Details = validationException.Errors;
                _logger.LogWarning(exception, "Validation Exception: {Message}", validationException.Message);
                break;

            case ApiException apiException:
                response.StatusCode = apiException.StatusCode;
                errorResponse.StatusCode = apiException.StatusCode;
                errorResponse.Message = apiException.Message;
                errorResponse.ErrorCode = apiException.ErrorCode;
                errorResponse.Details = apiException.Details;
                _logger.LogWarning(exception, "API Exception: {Message}", apiException.Message);
                break;

            case UnauthorizedAccessException:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Message = "No autorizado para realizar esta acción";
                errorResponse.ErrorCode = "UNAUTHORIZED";
                _logger.LogWarning(exception, "Unauthorized Access: {Message}", exception.Message);
                break;

            case KeyNotFoundException:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.Message = exception.Message;
                errorResponse.ErrorCode = "RESOURCE_NOT_FOUND";
                _logger.LogWarning(exception, "Resource Not Found: {Message}", exception.Message);
                break;

            case DbUpdateException dbException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Message = "Error al procesar la solicitud en la base de datos";
                errorResponse.ErrorCode = "DATABASE_ERROR";
                
                // Detectar violaciones de restricciones únicas
                if (dbException.InnerException?.Message.Contains("duplicate key") == true ||
                    dbException.InnerException?.Message.Contains("UNIQUE constraint") == true)
                {
                    errorResponse.Message = "El recurso ya existe en el sistema";
                    errorResponse.ErrorCode = "DUPLICATE_RESOURCE";
                }
                
                _logger.LogError(dbException, "Database Error: {Message}", dbException.Message);
                break;

            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.Message = _environment.IsDevelopment()
                    ? exception.Message
                    : "Ocurrió un error interno del servidor. Por favor, intente más tarde.";
                errorResponse.ErrorCode = "INTERNAL_SERVER_ERROR";
                
                if (_environment.IsDevelopment())
                {
                    errorResponse.StackTrace = exception.StackTrace;
                    errorResponse.Details = new
                    {
                        ExceptionType = exception.GetType().Name,
                        InnerException = exception.InnerException?.Message
                    };
                }
                
                _logger.LogError(exception, "Unhandled Exception: {Message}", exception.Message);
                break;
        }

        // Registrar error en la base de datos
        try
        {
            var userId = GetUserIdFromContext(context);
            await LogErrorToDatabase(errorLogRepository, exception, context, userId, errorResponse);
        }
        catch (Exception logException)
        {
            _logger.LogError(logException, "Error al registrar excepción en la base de datos");
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, jsonOptions);
        await response.WriteAsync(jsonResponse);
    }

    private Guid? GetUserIdFromContext(HttpContext context)
    {
        try
        {
            var userIdClaim = context.User?.FindFirst("sub")?.Value 
                           ?? context.User?.FindFirst("userId")?.Value;
            
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }
        }
        catch
        {
            // Ignorar errores al obtener el userId
        }

        return null;
    }

    private async Task LogErrorToDatabase(
        IRepository<ErrorLog> errorLogRepository,
        Exception exception,
        HttpContext context,
        Guid? userId,
        ErrorResponse errorResponse)
    {
        try
        {
            var errorLog = new ErrorLog
            {
                ErrorId = Guid.NewGuid(),
                ErrorMessage = exception.Message,
                StackTrace = exception.StackTrace,
                Source = exception.Source,
                UserId = userId,
                RequestPath = context.Request.Path,
                RequestMethod = context.Request.Method,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                Severity = DetermineSeverity(exception),
                IsNotified = false,
                CreatedAt = DateTime.UtcNow
            };

            await errorLogRepository.CreateAsync(errorLog);
        }
        catch
        {
            // No lanzar excepción si falla el logging
        }
    }

    private ErrorSeverity DetermineSeverity(Exception exception)
    {
        return exception switch
        {
            ApiException apiEx when apiEx.StatusCode >= 500 => ErrorSeverity.Critical,
            ApiException apiEx when apiEx.StatusCode >= 400 => ErrorSeverity.Medium,
            ValidationException => ErrorSeverity.Low,
            UnauthorizedAccessException => ErrorSeverity.Medium,
            KeyNotFoundException => ErrorSeverity.Low,
            _ => ErrorSeverity.High
        };
    }
}

/// <summary>
/// Modelo de respuesta de error estandarizado
/// </summary>
public class ErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public object? Details { get; set; }
    public string? StackTrace { get; set; }
    public DateTime Timestamp { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
}

