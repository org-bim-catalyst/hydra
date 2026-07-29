using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace AskLucy.Web.Middleware;

/// <summary>
/// Centralized exception handling, translating every failure into an RFC 9457 Problem
/// Details response (constitution &#167;6/&#167;8/&#167;22 and contracts/api-v1.md &#167; Error format).
/// Never exposes a stack trace or raw exception message to the client.
/// </summary>
public sealed class ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, type, title, detail) = Map(exception);

        if (statusCode >= 500)
        {
            ProblemDetailsMiddlewareLog.UnhandledException(logger, exception);
        }

        var problemDetails = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = statusCode,
            Detail = detail,
        };

        if (context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var correlationId))
        {
            problemDetails.Extensions["traceId"] = correlationId;
        }

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problemDetails);
    }

    private static (int StatusCode, string Type, string Title, string Detail) Map(Exception exception) => exception switch
    {
        ValidationException => (
            StatusCodes.Status400BadRequest,
            "https://hydra.bimcatalyst.com/problems/validation-failed",
            "Validation failed",
            "One or more fields are invalid."),

        DomainRuleViolationException domainEx => (
            StatusCodes.Status400BadRequest,
            "https://hydra.bimcatalyst.com/problems/domain-rule-violation",
            "Request violates a business rule",
            domainEx.Message),

        AiProviderUnavailableException => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-unavailable",
            "AI provider unavailable",
            "The AI service could not process your request. Please try again."),

        UnauthorizedAccessException => (
            StatusCodes.Status403Forbidden,
            "https://hydra.bimcatalyst.com/problems/forbidden",
            "Forbidden",
            "You do not have permission to perform this action."),

        KeyNotFoundException notFoundEx => (
            StatusCodes.Status404NotFound,
            "https://hydra.bimcatalyst.com/problems/not-found",
            "Not found",
            notFoundEx.Message),

        _ => (
            StatusCodes.Status500InternalServerError,
            "https://hydra.bimcatalyst.com/problems/internal-server-error",
            "An unexpected error occurred",
            "An unexpected error occurred. Please try again."),
    };
}

internal static partial class ProblemDetailsMiddlewareLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    public static partial void UnhandledException(ILogger logger, Exception exception);
}
