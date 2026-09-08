using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Conversations.Runtime;

internal static partial class CapabilityExecutorLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Capability {CapabilityKey} for chat {UserChatId} completed in {ElapsedMs}ms: {Outcome}")]
    public static partial void Completed(ILogger logger, string capabilityKey, Guid userChatId, long elapsedMs, string outcome);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Capability {CapabilityKey} for chat {UserChatId} was refused before running: {Reason}")]
    public static partial void Refused(ILogger logger, string capabilityKey, Guid userChatId, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Capability {CapabilityKey} for chat {UserChatId} threw despite its never-throws contract; the turn continued")]
    public static partial void Threw(ILogger logger, string capabilityKey, Guid userChatId, Exception exception);
}

/// <summary>What one capability invocation produced.</summary>
/// <param name="CapabilityKey">Which capability ran.</param>
/// <param name="Succeeded">Whether it produced a usable result.</param>
/// <param name="ResultJson">Its output, or the reason it failed — always something the narration step can report.</param>
/// <param name="Elapsed">How long it took, for the turn record and the cost budget.</param>
public sealed record CapabilityExecutionResult(
    string CapabilityKey,
    bool Succeeded,
    string ResultJson,
    TimeSpan Elapsed);

/// <summary>
/// Runs one capability on behalf of a conversation turn (specs/045 FR-015).
///
/// <para>
/// <b>Nothing is relaxed because the caller is a chat.</b> A capability reached from a
/// conversation passes the same gates it faces from the background agent runtime: its arguments
/// are validated against the Tier 3 schema the model never saw, its required permissions are
/// checked, and a High or Critical risk level without a matching policy stops it before it runs.
/// Those checks live here rather than inside each capability so a newly written one cannot skip
/// them.
/// </para>
///
/// <para>
/// <b>Isolation, not suppression</b> (constitution §2.VIII). Every capability documents a
/// never-throws contract, but turn integrity must not depend on another type keeping its promise.
/// A throw is caught, logged with its cause, and converted into a failed result the narration
/// step can report — so one capability failing costs its own step and nothing else (FR-040).
/// </para>
/// </summary>
public sealed class CapabilityExecutor(
    AgentPolicyEvaluator policyEvaluator,
    IJsonSchemaValidator schemaValidator,
    ILogger<CapabilityExecutor> logger)
{
    /// <summary>
    /// Ceiling on one capability's arguments. Generous for the shapes these actually take (a place
    /// name, a search query) and small enough that a runaway generation cannot be handed onward.
    /// </summary>
    private const long MaxArgumentBytes = 32 * 1024;

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        IConversationCapability capability,
        TurnContext turnContext,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // Grounding, second half. The first check confirmed the key exists; this one confirms the
        // arguments satisfy a schema the deciding model was never shown, which is what makes it an
        // independent verification rather than a formality (research.md D13).
        IReadOnlyList<string> schemaErrors;
        try
        {
            using var schemaDocument = JsonDocument.Parse(capability.InputSchemaJson);
            using var instanceDocument = JsonDocument.Parse(argumentsJson);
            schemaErrors = schemaValidator.Validate(
                schemaDocument.RootElement, instanceDocument.RootElement, MaxArgumentBytes);
        }
        catch (JsonException ex)
        {
            // Malformed arguments are a routing fault, not a crash: the decide step produced
            // something unusable, which the narration step can report like any other failure.
            var parseReason = $"its arguments were not valid JSON: {ex.Message}";
            CapabilityExecutorLog.Refused(logger, capability.Name, turnContext.UserChatId, parseReason);
            return Failed(capability, parseReason, startedAt);
        }

        if (schemaErrors.Count > 0)
        {
            var reason = $"the arguments did not satisfy its input contract: {string.Join("; ", schemaErrors)}";
            CapabilityExecutorLog.Refused(logger, capability.Name, turnContext.UserChatId, reason);
            return Failed(capability, reason, startedAt);
        }

        if (capability.RequiredPermissions.Any(p => !turnContext.GrantedPermissions.Contains(p)))
        {
            var missing = capability.RequiredPermissions.Where(p => !turnContext.GrantedPermissions.Contains(p));
            var reason = $"you do not have permission for this ({string.Join(", ", missing)})";
            CapabilityExecutorLog.Refused(logger, capability.Name, turnContext.UserChatId, reason);
            return Failed(capability, reason, startedAt);
        }

        // A risky capability needs a matching enabled policy. Without one it stops here rather
        // than running and reporting afterwards — the user confirms it as an offered action
        // instead, since a streaming turn has nowhere to suspend for out-of-band approval.
        if (capability.RiskLevel is Domain.Agents.AgentToolRiskLevel.High or Domain.Agents.AgentToolRiskLevel.Critical)
        {
            var policy = await policyEvaluator.FindMatchAsync(capability.Name, argumentsJson, cancellationToken);
            if (policy is null)
            {
                var reason = "this action needs approval that has not been granted";
                CapabilityExecutorLog.Refused(logger, capability.Name, turnContext.UserChatId, reason);
                return Failed(capability, reason, startedAt);
            }
        }

        try
        {
            using var input = JsonDocument.Parse(argumentsJson);
            var executionContext = new AgentToolExecutionContext(
                ExecutionId: Guid.CreateVersion7(),
                StepId: Guid.CreateVersion7(),
                UserId: turnContext.UserId ?? string.Empty,
                AgentId: Guid.Empty,
                AgentVersionId: Guid.Empty,
                UserChatId: turnContext.UserChatId);

            var result = await capability.ExecuteAsync(executionContext, input, cancellationToken);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);

            CapabilityExecutorLog.Completed(logger, capability.Name, turnContext.UserChatId,
                (long)elapsed.TotalMilliseconds, result.Succeeded ? "succeeded" : "failed");

            return new CapabilityExecutionResult(
                capability.Name,
                result.Succeeded,
                result.Succeeded ? result.Output?.RootElement.GetRawText() ?? "{}" : result.FailureReason ?? "it did not succeed",
                elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user cancelled. A user action, never a capability failure — it must propagate
            // and must never be recorded as this capability having gone wrong.
            throw;
        }
        catch (Exception ex)
        {
            CapabilityExecutorLog.Threw(logger, capability.Name, turnContext.UserChatId, ex);
            return Failed(capability, $"it failed unexpectedly: {ex.Message}", startedAt);
        }
    }

    private static CapabilityExecutionResult Failed(IConversationCapability capability, string reason, long startedAt) =>
        new(capability.Name, false, reason, System.Diagnostics.Stopwatch.GetElapsedTime(startedAt));
}
