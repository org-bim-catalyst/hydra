using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai;

/// <summary>
/// Shared structured security-event log for every admin AI-provider-catalog action
/// (constitution &#167;8) — mirrors <c>AskLucy.Application.Users.AdminActionLog</c>'s pattern
/// exactly. Never logs a credential value, even a partial one.
/// </summary>
internal static partial class AiAdminActionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Admin AI-provider action {Action} performed by {ActorUserId} against provider {ProviderId}: {Detail}")]
    public static partial void AdminAiProviderActionPerformed(ILogger logger, string action, string actorUserId, Guid providerId, string detail);

    /// <summary>specs/008-ai-model-catalog-management FR-002 — a manual model status change.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Admin AI-model status change performed by {ActorUserId} against model {ModelId}: {OldStatus} -> {NewStatus}")]
    public static partial void AdminAiModelStatusChanged(ILogger logger, string actorUserId, Guid modelId, string oldStatus, string newStatus);

    /// <summary>specs/008-ai-model-catalog-management FR-007/FR-008 — a confirmed sync diff applied to a provider's catalog.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Admin AI-model sync applied by {ActorUserId} for provider {ProviderId}: {AddedCount} added, {MarkedUnavailableCount} marked unavailable")]
    public static partial void AdminAiModelSyncApplied(ILogger logger, string actorUserId, Guid providerId, int addedCount, int markedUnavailableCount);
}
