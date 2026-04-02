using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Dressfield.API.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;
        var (status, title, detail) = MapException(exception);

        _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{status}",
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private (int Status, string Title, string Detail) MapException(Exception exception)
    {
        return exception switch
        {
            ValidationException validationException => (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                BuildValidationDetail(validationException)),

            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Authentication failed."),

            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Not found",
                exception.Message),

            InvalidOperationException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                _environment.IsDevelopment()
                    ? exception.Message
                    : "The request could not be processed."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Server error",
                _environment.IsDevelopment()
                    ? exception.Message
                    : "An unexpected server error occurred.")
        };
    }

    private static string BuildValidationDetail(ValidationException validationException)
    {
        var messages = validationException.Errors
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToArray();

        return messages.Length == 0
            ? "Request validation failed."
            : string.Join("; ", messages);
    }
}
