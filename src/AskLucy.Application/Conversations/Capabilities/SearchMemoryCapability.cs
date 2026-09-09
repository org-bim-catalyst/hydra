using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Looks up what Lucy remembers about this user (specs/045 FR-013).
///
/// <para>
/// <b>Never offered</b>, deliberately: recalling a stored preference is an internal step on the
/// way to answering, not a destination a user would pick from a menu. A row reading "check what I
/// remember" would advertise plumbing.
/// </para>
/// </summary>
public sealed class SearchMemoryCapability(IMemoryService memoryService) : IConversationCapability
{
    public const string CapabilityKey = "search_memory";

    public string Name => CapabilityKey;

    public string Description => "Retrieves durable facts and preferences remembered about this user.";

    public string WhenToUse =>
        "Use when the user asks what you remember about them, refers to a preference or working " +
        "style they set before, or says \"as I told you\" or \"like last time\" — or when answering " +
        "well depends on recalling something outside this conversation.";

    public string ArgumentHint => "what to recall";

    public string UsageGuidance =>
        "Use recalled facts to shape the answer rather than reciting them back. If nothing " +
        "relevant is remembered, proceed without comment — announcing an empty memory lookup tells " +
        "the user about the machinery instead of answering them.";

    public string Label => "Check what I remember";

    public string OfferDescription => "Recall what you have told me before.";

    public string AcknowledgementTemplate => "Let me check what I remember.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ReadMemory];

    public string InputSchemaJson =>
        """{"type":"object","required":["query"],"properties":{"query":{"type":"string","minLength":1}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"outcome":{"type":"string"},"contextText":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Memory;

    public bool IsAvailable(TurnContext context) => context.IsMemoryAvailable && context.UserId is not null;

    /// <summary>Never offered — an internal lookup, not a user-facing choice.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("query", out var queryElement) ||
            queryElement.GetString() is not { Length: > 0 } query)
        {
            return AgentToolResult.Failure("A non-empty query is required.");
        }

        try
        {
            var outcome = await memoryService.RetrieveRelevantMemoriesAsync(
                context.UserId, context.UserChatId ?? Guid.Empty, projectId: null, query, cancellationToken);

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                outcome = outcome.Type.ToString(),
                contextText = outcome.ContextText,
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentToolResult.Failure($"The memory lookup failed: {ex.Message}");
        }
    }
}
