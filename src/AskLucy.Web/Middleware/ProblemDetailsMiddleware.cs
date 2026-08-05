using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        // FR-028 (specs/002-chat-history-management): access-denial responses are logged as
        // security events here — the single cross-cutting boundary every request passes
        // through (constitution §3) — rather than duplicated into each Application-layer
        // ownership check (e.g. ChatOwnershipGuard), which would also require threading an
        // ILogger into every command/query handler that calls it.
        if (exception is KeyNotFoundException or UnauthorizedAccessException)
        {
            ProblemDetailsMiddlewareLog.AccessDenied(logger, context.Request.Path, statusCode);
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

        // FR-028: pass the vendor's own Retry-After hint through when it supplied one.
        if (exception is AiProviderRateLimitedException { RetryAfter: { } retryAfter })
        {
            context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // FR-029, contracts/document-processing-api.md: the client needs a machine-readable
        // reason code (not just a human-readable Detail string) to distinguish this specific
        // 409 case from any other conflict.
        if (exception is AskLucy.Domain.Documents.ProcessingNotInFailedStateException)
        {
            problemDetails.Extensions["reason"] = "NotInFailedState";
        }

        // FR-041, contracts/document-versions-folders-api.md: same machine-readable-reason pattern as above.
        if (exception is AskLucy.Domain.Documents.VersionUploadInProgressException)
        {
            problemDetails.Extensions["reason"] = "VersionUploadInProgress";
        }

        context.Response.StatusCode = statusCode;
        // WriteAsJsonAsync's no-content-type overload unconditionally overwrites
        // Response.ContentType to "application/json" — passing it explicitly here is what
        // actually makes every error response RFC 9457-compliant (constitution §6); setting
        // ContentType beforehand alone (the previous code) was silently discarded.
        await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
    }

    private static (int StatusCode, string Type, string Title, string Detail) Map(Exception exception) => exception switch
    {
        ValidationException => (
            StatusCodes.Status400BadRequest,
            "https://hydra.bimcatalyst.com/problems/validation-failed",
            "Validation failed",
            "One or more fields are invalid."),

        // constitution §5: a stale RowVersion MUST be handled explicitly, not left to bubble
        // as a 500 — surfaced here (cross-cutting, per constitution §3) rather than a
        // try/catch duplicated into every command handler that mutates a concurrency-tracked
        // entity (specs/002-chat-history-management tasks.md T073, research.md Topic 10).
        DbUpdateConcurrencyException => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/concurrency-conflict",
            "Concurrency conflict",
            "This item was modified by another request. Please reload and try again."),

        DomainRuleViolationException domainEx => (
            StatusCodes.Status400BadRequest,
            "https://hydra.bimcatalyst.com/problems/domain-rule-violation",
            "Request violates a business rule",
            domainEx.Message),

        DuplicateResourceException duplicateEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/duplicate-resource",
            "Duplicate resource",
            duplicateEx.Message),

        AskLucy.Domain.Documents.ProcessingNotInFailedStateException processingConflictEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/processing-not-in-failed-state",
            "Processing job not in failed state",
            processingConflictEx.Message),

        AskLucy.Domain.Documents.VersionUploadInProgressException versionConflictEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/version-upload-in-progress",
            "A version upload is already in progress",
            versionConflictEx.Message),

        AiProviderUnavailableException => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-unavailable",
            "AI provider unavailable",
            "The AI service could not process your request. Please try again."),

        // specs/005-multi-provider-ai-engine FR-028/FR-029 (research.md Decision 9): every
        // vendor's authentication/rate-limit failures translate to these two shared types,
        // regardless of which provider produced them.
        AiProviderAuthenticationException => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-authentication-failed",
            "AI provider authentication failed",
            "The AI provider rejected the configured credential. An administrator needs to check the provider's API key."),

        AiProviderRateLimitedException => (
            StatusCodes.Status429TooManyRequests,
            "https://hydra.bimcatalyst.com/problems/ai-provider-rate-limited",
            "AI provider rate limited",
            "The AI provider is rate-limiting requests right now. Please try again shortly."),

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Access denied: {Path} returned {StatusCode}")]
    public static partial void AccessDenied(ILogger logger, PathString path, int statusCode);
}
