using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using AskLucy.Domain.OperationalFailures;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Web.Middleware;

/// <summary>
/// Centralized exception handling, translating every failure into an RFC 9457 Problem
/// Details response (constitution &#167;6/&#167;8/&#167;22 and contracts/api-v1.md &#167; Error format).
/// Never exposes a stack trace or raw exception message to the client.
/// </summary>
public sealed class ProblemDetailsMiddleware(
    RequestDelegate next,
    ILogger<ProblemDetailsMiddleware> logger,
    IOperationalFailureRecorder recorder,
    IFailureClassifier classifier)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (IsClientDisconnect(context, ex))
        {
            // The caller went away mid-request — a page refresh, a closed tab, a superseded
            // request. Nothing failed on the server and there is nobody left to send a response
            // to, so this is recorded as the routine event it is rather than reported as an
            // error. Left unhandled it surfaced as "Unhandled exception" at Error level on every
            // refresh of a page holding an open SSE stream, burying real failures in the log.
            ProblemDetailsMiddlewareLog.ClientDisconnected(logger, context.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    /// <summary>
    /// A cancellation caused by the caller disconnecting, rather than by anything the server
    /// decided. Checked against this request's own <see cref="HttpContext.RequestAborted"/> —
    /// an <see cref="OperationCanceledException"/> raised for any other reason is a real failure
    /// and must still be mapped and reported.
    /// </summary>
    private static bool IsClientDisconnect(HttpContext context, Exception exception) =>
        exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested;

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, type, title, detail) = Map(exception);

        // specs/043 FR-010/FR-014/FR-015a. An administrator gets the classifier's own prose -
        // built from the classification alone, never from the vendor body - plus a
        // machine-readable code for the admin UI to branch on. Everyone else keeps the
        // pre-existing cause-free message and gets no extension at all.
        var isProviderFailure = exception is AiProviderException;
        var isAdministrator = IsAdministrator(context);
        var discloseClassification = isProviderFailure && isAdministrator;
        if (discloseClassification)
        {
            detail = exception.Message;
        }
        else if (isAdministrator && AdministratorDetail(exception) is { } administratorDetail)
        {
            detail = administratorDetail;
        }

        if (statusCode >= 500)
        {
            ProblemDetailsMiddlewareLog.UnhandledException(logger, exception);
        }

        // specs/043 FR-014: every provider failure is recorded with its classification, whatever
        // status it maps to. Without this the 4xx-mapped ones (rate limit, quota, invalid
        // request) left no server-side trace at all, since only >= 500 was logged above.
        if (exception is AiProviderException loggedFailure)
        {
            ProblemDetailsMiddlewareLog.ProviderFailureSurfaced(
                logger, loggedFailure.Kind, statusCode, context.Request.Path);
        }

        // specs/074 FR-001 — the boundary puts every system-side failure on the admin trail, after
        // the log line above so the two share a correlation id. A site that already recorded the
        // exception marked it (research D11), so a failure is never counted twice.
        if ((isProviderFailure || statusCode >= 500) && !exception.IsOperationalFailureRecorded())
        {
            RecordFailure(context, exception);
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

        if (context.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var correlationId))
        {
            problemDetails.Extensions["traceId"] = correlationId;
        }

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        // FR-028, extended by specs/043 FR-012: any classified failure may carry the vendor's
        // own Retry-After hint - an exhausted quota often does, not just a rate limit. Never
        // invented when the vendor did not supply one.
        if (exception is AiProviderException { RetryAfter: { } retryAfter })
        {
            context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (discloseClassification && exception is AiProviderException classified)
        {
            problemDetails.Extensions["providerFailure"] = new Dictionary<string, object?>
            {
                ["kind"] = classified.Kind.ToString(),
                ["canAdministratorAct"] = CanAdministratorAct(classified.Kind),
                ["retryAfterSeconds"] = classified.RetryAfter is { } hint ? (int)hint.TotalSeconds : null,
            };
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

        // spec.md FR-005/FR-065 (specs/021-mcp-integration): the admin UI lists exactly which
        // agent/tool pairs must be cleared before removal can proceed, without a follow-up request.
        if (exception is AskLucy.Domain.Mcp.McpServerHasReferencesException hasReferencesException)
        {
            problemDetails.Extensions["referencingAgentTools"] = hasReferencesException.ReferencingAgentTools
                .Select(r => new { agentId = r.AgentId, toolName = r.ToolName })
                .ToArray();
        }

        // spec.md FR-016/SC-009 (specs/022-workflow-orchestration-engine): every validation
        // violation, so the Designer's validation panel can render them all without re-requesting.
        if (exception is AskLucy.Domain.Workflows.WorkflowValidationFailedException workflowValidationFailedException)
        {
            problemDetails.Extensions["violations"] = workflowValidationFailedException.Violations
                .Select(v => new { nodeKey = v.NodeKey, message = v.Message })
                .ToArray();
        }

        // Nothing can be reported to the client once the response is on the wire: the status
        // line and headers are already sent, and a streaming endpoint (the chat SSE stream) has
        // usually written content too. Setting StatusCode here throws
        // "StatusCode cannot be set because the response has already started" — a SECOND
        // exception, raised from inside the error handler, which replaces the real one in the
        // log and escapes to Kestrel, which then resets the connection. That reset is what the
        // browser reported as ERR_HTTP2_PROTOCOL_ERROR on a 200 response. The failure is still
        // recorded above; the connection simply ends, which is the only honest outcome left.
        if (context.Response.HasStarted)
        {
            ProblemDetailsMiddlewareLog.ResponseAlreadyStarted(logger, context.Request.Path, statusCode);
            return;
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

        // Application-layer-safe equivalent of DbUpdateConcurrencyException above (constitution
        // §3 forbids Application from referencing EF Core) — specs/055-role-management's
        // AssignRoleCommandHandler raises this, never the EF Core type directly.
        AskLucy.Domain.Common.ConcurrencyConflictException concurrencyEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/concurrency-conflict",
            "Concurrency conflict",
            concurrencyEx.Message),

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

        AskLucy.Domain.Memory.MemoryNotPendingApprovalException memoryApprovalConflictEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/memory-not-pending-approval",
            "Memory is not pending approval",
            memoryApprovalConflictEx.Message),

        AskLucy.Domain.Memory.MemoryConflictNotPendingException memoryResolveConflictEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/memory-conflict-not-pending",
            "Memory conflict is not awaiting resolution",
            memoryResolveConflictEx.Message),

        // specs/045-conversational-agent-runtime US3, contracts/suggested-actions-api.md §1 —
        // the referenced offer is not the newest unanswered one, isn't in this chat, or no longer
        // carries the selected row (FR-029).
        AskLucy.Domain.Conversations.ConversationActionStaleException conversationActionStaleEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/conversation-action-stale",
            "That offer is no longer current",
            conversationActionStaleEx.Message),

        // Same contract — the row exists, but its precondition no longer holds against a freshly
        // built turn context (FR-028).
        AskLucy.Domain.Conversations.ConversationActionUnavailableException conversationActionUnavailableEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/conversation-action-unavailable",
            "That action is no longer available",
            conversationActionUnavailableEx.Message),

        // Same contract — the row's key matches no registered capability at all (or names a flow
        // variant, which has no registry yet) — a protocol error, hence 400 rather than 409.
        AskLucy.Domain.Conversations.ConversationActionUnknownException conversationActionUnknownEx => (
            StatusCodes.Status400BadRequest,
            "https://hydra.bimcatalyst.com/problems/conversation-action-unknown",
            "Unrecognised action",
            conversationActionUnknownEx.Message),

        // spec.md FR-042/FR-043 (specs/020-ai-agent-framework): a rate/capacity limit, not an
        // invalid request — 429, not DomainRuleViolationException's generic 400.
        AskLucy.Domain.Agents.AgentConcurrencyLimitExceededException agentConcurrencyEx => (
            StatusCodes.Status429TooManyRequests,
            "https://hydra.bimcatalyst.com/problems/agent-concurrency-limit-exceeded",
            "Agent execution concurrency limit exceeded",
            agentConcurrencyEx.Message),

        // spec.md FR-069/FR-070 (specs/022-workflow-orchestration-engine): mirrors the Agent
        // Framework's identical concurrency-cap precedent — a rate/capacity limit, not an invalid
        // request.
        AskLucy.Domain.Workflows.WorkflowConcurrencyLimitExceededException workflowConcurrencyEx => (
            StatusCodes.Status429TooManyRequests,
            "https://hydra.bimcatalyst.com/problems/workflow-concurrency-limit-exceeded",
            "Workflow execution concurrency limit exceeded",
            workflowConcurrencyEx.Message),

        // spec.md FR-016/SC-009 (specs/022-workflow-orchestration-engine): the request is
        // well-formed, the workflow graph itself is invalid — 422, with every violation surfaced
        // so the Designer's validation panel can list them without a follow-up request.
        AskLucy.Domain.Workflows.WorkflowValidationFailedException workflowValidationEx => (
            StatusCodes.Status422UnprocessableEntity,
            "https://hydra.bimcatalyst.com/problems/workflow-validation-failed",
            "Workflow validation failed",
            workflowValidationEx.Message),

        // spec.md FR-050 (specs/021-mcp-integration, research.md Decision 8): the request is
        // well-formed, the destination is simply disallowed — 422, not DomainRuleViolationException's
        // generic 400.
        AskLucy.Domain.Mcp.McpEndpointNotAllowedException endpointNotAllowedEx => (
            StatusCodes.Status422UnprocessableEntity,
            "https://hydra.bimcatalyst.com/problems/mcp-endpoint-not-allowed",
            "Endpoint not allowed",
            endpointNotAllowedEx.Message),

        // spec.md FR-005 (specs/021-mcp-integration, clarification): removal is blocked, not
        // invalid — 422, with the referencing agent/tool pairs surfaced as a machine-readable
        // extension so the admin UI can list them without a follow-up request.
        AskLucy.Domain.Mcp.McpServerHasReferencesException hasReferencesEx => (
            StatusCodes.Status422UnprocessableEntity,
            "https://hydra.bimcatalyst.com/problems/mcp-server-has-references",
            "MCP server has references",
            hasReferencesEx.Message),

        // specs/043 FR-001, contracts/provider-failure-classification.md §3: one arm for every
        // provider-originated failure, switching on the classification rather than on the
        // concrete exception type. This replaced five near-identical arms - adding a tenth
        // classification now needs no change here beyond a row in MapProviderFailure.
        //
        // The detail produced here is the *generic* one every principal may see; an
        // administrator's specific detail and the machine-readable extension are applied in
        // HandleAsync, gated on role (FR-015a).
        AiProviderException providerFailure => MapProviderFailure(providerFailure),

        // specs/057 follow-up: image generation has no platform-default fallback (a chat model
        // cannot draw), so an unassigned or unusable ImageGeneration capability is a setup gap
        // the user should be told about plainly — 503, not a generic 500.
        AskLucy.Application.Ai.AiCapabilityNotConfiguredException => (
            StatusCodes.Status503ServiceUnavailable,
            "https://hydra.bimcatalyst.com/problems/ai-capability-not-configured",
            "AI capability not configured",
            // specs/074 FR-002 — the setup gap is administrator state; an administrator gets the
            // specific sentence from AdministratorDetail.
            UserFacingFailureText.Later),

        // specs/072 FR-019: the deployment target isn't configured on this server. 400, not 503 —
        // the contract's own status, so the dialog can tell it apart from a transient outage. The
        // message never names which setting is missing or what any configured value is.
        AskLucy.Application.CustomModels.Abstractions.CustomModelDeploymentNotConfiguredException notConfiguredEx => (
            StatusCodes.Status400BadRequest,
            "https://hydra.bimcatalyst.com/problems/deployment-not-configured",
            "Deployment not configured",
            notConfiguredEx.Message),

        // specs/072 contracts/admin-custom-models.md: cancelling a deployment that already finished.
        AskLucy.Domain.CustomModels.CustomModelNotInProgressException notInProgressEx => (
            StatusCodes.Status409Conflict,
            "https://hydra.bimcatalyst.com/problems/custom-model-not-in-progress",
            "Deployment not in progress",
            notInProgressEx.Message),

        // The provider answered, but not with a usable image (malformed data, unsupported
        // format, oversized) — upstream's fault, same 502 family as a provider failure.
        AskLucy.Application.Ai.Images.InvalidGeneratedImageException => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-unavailable",
            "AI provider unavailable",
            "The AI service returned an image that couldn't be used. Please try again."),

        // specs/040-composer-interaction-bug-fixes US6: any HttpRequestException that escapes
        // the AI providers without being caught and re-thrown as a typed AiProvider*Exception
        // (e.g. a network failure during a transcription upload) still surfaces a classified,
        // actionable 502 rather than the generic 500, without exposing the raw exception message.
        HttpRequestException => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-unavailable",
            "AI provider unavailable",
            "The AI service could not process your request. Please try again."),

        // specs/027-immersive-viewer-platform contracts/weather-api.md: mirrors the
        // AiProviderUnavailableException → 502 pattern above — the upstream weather/reverse-
        // geocoding service errored, timed out, or returned something unparseable.
        AskLucy.Application.Abstractions.WeatherProviderUnavailableException => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/weather-provider-unavailable",
            "Weather provider unavailable",
            "The weather service could not process this request. Please try again."),

        // specs/052-solar-analysis contracts/building-footprints-endpoint.md — Overpass exhausted
        // its retries. 503, not 502: this is the contract's own stated status (distinct from the
        // AI-provider/weather 502 convention above), so the client's `partial` state (FR-014,
        // sun path continues) can be driven off a status code specific to this endpoint.
        AskLucy.Application.Buildings.BuildingProviderUnavailableException => (
            StatusCodes.Status503ServiceUnavailable,
            "https://hydra.bimcatalyst.com/problems/building-provider-unavailable",
            "Building data unavailable",
            "Building data is temporarily unavailable."),

        // specs/045-conversational-agent-runtime FR-034, contracts/system-agent-provisioning.md
        // §3 — a platform-provisioned agent (lucy.orchestrator and its sub-agents) rejecting every
        // user mutation path.
        AskLucy.Domain.Agents.SystemAgentImmutableException systemAgentImmutableEx => (
            StatusCodes.Status403Forbidden,
            "https://hydra.bimcatalyst.com/problems/system-agent-immutable",
            "System agent is immutable",
            systemAgentImmutableEx.Message),

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

    /// <summary>
    /// The specific detail an administrator gets instead of the cause-free one, for failures other
    /// than provider failures (whose administrator detail is the classifier's own prose).
    /// </summary>
    private static string? AdministratorDetail(Exception exception) => exception switch
    {
        AskLucy.Application.Ai.AiCapabilityNotConfiguredException =>
            "Image generation isn't set up yet. An administrator needs to assign an image model on the AI Capabilities page.",
        _ => null,
    };

    /// <summary>
    /// Never throws: <see cref="IOperationalFailureRecorder.Record"/> cannot, and nothing else here
    /// does I/O. The endpoint's route template, not its path, names the operation, so ids in the
    /// URL never split one failure into many incidents.
    /// </summary>
    private void RecordFailure(HttpContext context, Exception exception)
    {
        var kind = classifier.Classify(exception, context.RequestAborted);
        if (kind is null)
        {
            return;
        }

        var isChat = context.Request.Path.StartsWithSegments("/api/v1/ai", StringComparison.OrdinalIgnoreCase);
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        recorder.Record(new OperationalFailureReport
        {
            Engine = isChat ? OperationalFailureEngine.Chat : OperationalFailureEngine.AiProvider,
            Operation = route is null ? context.Request.Method : $"{context.Request.Method} /{route.TrimStart('/')}",
            Kind = kind.Value,
            Reason = exception is AiProviderException provider ? provider.Message : classifier.FallbackReason(exception),
            Exception = exception,
            References = new OperationalFailureReferences
            {
                UserId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier),
                ChatId = RouteGuid(context, "chatId"),
            },
        });
    }

    private static Guid? RouteGuid(HttpContext context, string key) =>
        context.GetRouteValue(key) is { } value && Guid.TryParse(value.ToString(), out var id) ? id : null;

    /// <summary>
    /// contracts/provider-failure-classification.md §3 - the status and problem type for each
    /// classification. A non-administrator's detail is one of the two calm sentences (specs/074
    /// research D9); an administrator's is the classifier's own prose, applied in HandleAsync.
    /// </summary>
    private static (int StatusCode, string Type, string Title, string Detail) MapProviderFailure(AiProviderException exception)
    {
        var (statusCode, type, title) = MapProviderFailureStatus(exception);
        return (statusCode, type, title, UserFacingFailureText.For(OperationalFailureKinds.FromProvider(exception.Kind)));
    }

    private static (int StatusCode, string Type, string Title) MapProviderFailureStatus(AiProviderException exception) => exception.Kind switch
    {
        AiProviderFailureKind.CredentialRejected => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-authentication-failed",
            "AI provider authentication failed"),

        AiProviderFailureKind.CredentialUnreadable => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-credential-unreadable",
            "AI provider credential unreadable"),

        // specs/068 - deliberately NOT a 502 "please try again". Every other kind here describes
        // something that happened to a request we actually made; this one means no request was
        // made at all, because the provider is switched off or has no key. Telling a user to try
        // again is telling them to repeat an action that cannot succeed until an administrator
        // changes a setting, and a client that honours the advice retries forever. 503 says
        // "unavailable", which is what it is, and the detail says so without naming the cause -
        // which provider is disabled is administrator state, not end-user state (FR-015a).
        AiProviderFailureKind.NotConfigured => (
            StatusCodes.Status503ServiceUnavailable,
            "https://hydra.bimcatalyst.com/problems/ai-provider-not-configured",
            "AI provider not configured"),

        AiProviderFailureKind.QuotaExhausted => (
            StatusCodes.Status429TooManyRequests,
            "https://hydra.bimcatalyst.com/problems/ai-provider-quota-exhausted",
            "AI provider quota exhausted"),

        AiProviderFailureKind.RateLimited => (
            StatusCodes.Status429TooManyRequests,
            "https://hydra.bimcatalyst.com/problems/ai-provider-rate-limited",
            "AI provider rate limited"),

        AiProviderFailureKind.UsageRestricted => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-usage-restricted",
            "AI provider usage restricted"),

        AiProviderFailureKind.RequestInvalid => (
            StatusCodes.Status400BadRequest,
            "https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid",
            "AI provider rejected the request"),

        AiProviderFailureKind.ResponseNotUnderstood => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-response-invalid",
            "AI provider response not understood"),

        _ => (
            StatusCodes.Status502BadGateway,
            "https://hydra.bimcatalyst.com/problems/ai-provider-unavailable",
            "AI provider unavailable"),
    };

    /// <summary>
    /// FR-011 - whether an administrator can fix this now, or has to wait it out. Drives the
    /// call-to-action the admin UI renders, so it must not claim an unfixable condition is
    /// actionable.
    /// </summary>
    private static bool CanAdministratorAct(AiProviderFailureKind kind) => kind switch
    {
        AiProviderFailureKind.CredentialRejected => true,
        AiProviderFailureKind.CredentialUnreadable => true,
        AiProviderFailureKind.NotConfigured => true,
        AiProviderFailureKind.UsageRestricted => true,
        _ => false,
    };

    /// <summary>
    /// FR-015a. Same role test as Program.cs's privileged-user check - the classification is
    /// administrator-only, and a non-administrator must not be able to read it out of the
    /// response either as prose or as the machine-readable extension.
    /// </summary>
    private static bool IsAdministrator(HttpContext context) =>
        context.User?.IsInRole("Administrator") == true || context.User?.IsInRole("Super User") == true;
}

internal static partial class ProblemDetailsMiddlewareLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    public static partial void UnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI provider failure surfaced: kind={Kind}, status={StatusCode}, path={Path}")]
    public static partial void ProviderFailureSurfaced(ILogger logger, AiProviderFailureKind kind, int statusCode, PathString path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Access denied: {Path} returned {StatusCode}")]
    public static partial void AccessDenied(ILogger logger, PathString path, int statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client disconnected before {Path} completed")]
    public static partial void ClientDisconnected(ILogger logger, PathString path);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Path} failed after the response had already started; the {StatusCode} Problem Details response could not be sent and the connection will be reset")]
    public static partial void ResponseAlreadyStarted(ILogger logger, PathString path, int statusCode);
}
