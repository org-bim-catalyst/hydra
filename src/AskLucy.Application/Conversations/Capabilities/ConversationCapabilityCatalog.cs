using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Options;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// One Tier 1 index entry — everything the deciding model is told about a capability, and
/// nothing more (research.md D13). Note the absence of a schema: that is Tier 3 and reaches no
/// model, which is what lets the grounder validate arguments the model never saw.
/// </summary>
public sealed record CapabilityIndexEntry(string Key, string Description, string WhenToUse, string ArgumentHint);

/// <summary>
/// Resolves which capabilities Lucy may see, invoke and offer this turn (specs/045 FR-010 –
/// FR-014, FR-025b).
///
/// <para>
/// Reads <see cref="AgentToolCatalog"/> live on every call rather than caching, so an MCP server
/// going active or inactive is reflected in the very next turn without a restart — the same
/// property the underlying catalog already guarantees.
/// </para>
/// </summary>
public sealed class ConversationCapabilityCatalog(
    AgentToolCatalog toolCatalog,
    CapabilityIndexRetriever indexRetriever,
    IOptions<ConversationRuntimeOptions> options)
{
    /// <summary>
    /// Every capability that <b>could run</b> this turn (FR-014). The set the orchestrator is
    /// allowed to invoke from.
    /// </summary>
    public IReadOnlyList<IConversationCapability> AvailableFor(TurnContext context) =>
        [.. toolCatalog.All
            .OfType<IConversationCapability>()
            .Where(c => IsEntitled(c, context) && c.IsAvailable(context))];

    /// <summary>
    /// Every capability <b>worth suggesting</b> (FR-025b) — a strictly smaller set than
    /// <see cref="AvailableFor"/>, and usually much smaller. Most registered capabilities are
    /// never offerable at all: they are reached when the user asks, not advertised.
    /// </summary>
    public IReadOnlyList<IConversationCapability> OfferableFor(TurnContext context, TurnOutcome justCompleted) =>
        [.. AvailableFor(context)
            .Where(c => !justCompleted.WasInvokedThisTurn(c.Name))
            .Where(c => !justCompleted.WasOfferedAndIgnored(c.Name))
            .Where(c => c.IsOfferable(context, justCompleted))];

    /// <summary>
    /// The Tier 1 index for this turn — the only capabilities the deciding model may see or name.
    /// <para>
    /// Below <see cref="ConversationRuntimeOptions.IndexRetrievalThreshold"/> the whole set is
    /// listed. Above it — which happens as soon as MCP servers are connected — embedding
    /// retrieval narrows it, because a catalogue of sixty tools would otherwise cost ~1,500
    /// tokens of prompt on every single turn (research.md D14).
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<CapabilityIndexEntry>> BuildIndexAsync(
        TurnContext context, string userMessage, CancellationToken cancellationToken)
    {
        var available = AvailableFor(context);
        if (available.Count <= options.Value.IndexRetrievalThreshold)
        {
            return [.. available.Select(ToEntry)];
        }

        var narrowed = await indexRetriever.NarrowAsync(available, context, userMessage, cancellationToken);
        return [.. narrowed.Select(ToEntry)];
    }

    /// <summary>
    /// Resolves a key for dispatch (FR-028). Returns the capability regardless of availability —
    /// the caller re-checks that against a fresh context, so that "no such capability" (400) and
    /// "no longer possible" (409) stay distinguishable to the user.
    /// </summary>
    public IConversationCapability? Find(string capabilityKey) =>
        toolCatalog.All.OfType<IConversationCapability>()
            .FirstOrDefault(c => string.Equals(c.Name, capabilityKey, StringComparison.Ordinal));

    /// <summary>
    /// FR-011 rule 3, enforced here rather than inside each capability.
    /// <para>
    /// Deliberately central: an entitlement check that every implementation has to remember is an
    /// entitlement check that a future implementation will forget, and forgetting it means
    /// offering a user something their permissions or plan do not include. Preconditions and
    /// non-redundancy stay with the capability, because only it knows those.
    /// </para>
    /// </summary>
    private static bool IsEntitled(IConversationCapability capability, TurnContext context) =>
        capability.RequiredPermissions.Count == 0 ||
        capability.RequiredPermissions.All(context.GrantedPermissions.Contains);

    private static CapabilityIndexEntry ToEntry(IConversationCapability capability) =>
        new(capability.Name, capability.Description, capability.WhenToUse, capability.ArgumentHint);
}
