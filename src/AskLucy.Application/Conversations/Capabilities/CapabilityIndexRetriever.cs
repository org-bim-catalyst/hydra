using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Conversations.Capabilities;

internal static partial class CapabilityIndexRetrieverLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Capability index retrieval is unavailable ({Reason}); falling back to the full index of {CapabilityCount} capabilities")]
    public static partial void FallingBackToFullIndex(ILogger logger, string reason, int capabilityCount, Exception? exception);
}

/// <summary>
/// Narrows a large Tier 1 capability index to the entries most relevant to what the user just
/// said (specs/045 FR-024a, research.md D14).
///
/// <para>
/// <b>It never decides anything.</b> It chooses which entries the deciding model is shown; the
/// model still selects. A retrieval miss therefore costs a missed option, never a wrong action —
/// which is the property that makes an approximate, embedding-based step acceptable on a path
/// where correctness matters.
/// </para>
///
/// <para>
/// <b>Context-gated capabilities are always retained</b>, regardless of similarity score. Those
/// are exactly the ones that matter most in the current state — a boundary when a location has
/// just been confirmed — and losing one to a cosine comparison would be the worst possible miss.
/// Only capabilities that are available unconditionally compete for the remaining slots.
/// </para>
///
/// <para>
/// Uses the in-process ONNX encoder, which needs no network and adds ~10 ms. A <i>generative</i>
/// model was considered and rejected for this job: on shared hosting a 0.5B decoder would take
/// seconds per decision, against a budget measured in hundreds of milliseconds. An encoder is the
/// right size of tool for routing.
/// </para>
/// </summary>
public sealed class CapabilityIndexRetriever(
    IEmbeddingService embeddingService,
    IOptions<ConversationRuntimeOptions> options,
    ILogger<CapabilityIndexRetriever> logger)
{
    public async Task<IReadOnlyList<IConversationCapability>> NarrowAsync(
        IReadOnlyList<IConversationCapability> available,
        TurnContext context,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var topN = options.Value.IndexRetrievalTopN;
        if (available.Count <= topN || string.IsNullOrWhiteSpace(userMessage))
        {
            return available;
        }

        // Split before embedding: context-gated entries are kept outright, so only the
        // unconditional ones need scoring, and the expensive step shrinks accordingly.
        var alwaysKeep = available.Where(c => IsContextGated(c, context)).ToList();
        var candidates = available.Except(alwaysKeep).ToList();

        var remainingSlots = topN - alwaysKeep.Count;
        if (remainingSlots <= 0 || candidates.Count == 0)
        {
            return alwaysKeep.Count > 0 ? alwaysKeep : available;
        }

        try
        {
            var messageVector = (await embeddingService.EmbedAsync(userMessage, cancellationToken)).Vector;

            // The index entry is what the model matches against, so it is also what we embed —
            // scoring the description alone would rank on purpose while the model routes on
            // trigger words.
            var entryTexts = candidates.Select(c => $"{c.Description} {c.WhenToUse}").ToList();
            var entryVectors = await embeddingService.EmbedBatchAsync(entryTexts, cancellationToken);

            var ranked = candidates
                .Select((capability, i) => (capability, score: CosineSimilarity(messageVector, entryVectors[i].Vector)))
                .OrderByDescending(x => x.score)
                .Take(remainingSlots)
                .Select(x => x.capability);

            return [.. alwaysKeep, .. ranked];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled. Not a retrieval failure, and must not be reported as one.
            throw;
        }
        catch (Exception ex)
        {
            // Retrieval is an optimisation, never a dependency: the model file may simply not be
            // deployed. Falling back to the full index costs prompt tokens and nothing else, so a
            // failure here must never take the turn down with it (constitution §2.VIII — logged,
            // not swallowed, and the user's turn proceeds).
            CapabilityIndexRetrieverLog.FallingBackToFullIndex(logger, ex.GetType().Name, available.Count, ex);
            return available;
        }
    }

    /// <summary>
    /// Whether a capability's availability depends on current turn state rather than being
    /// unconditional. Context-gated capabilities survive narrowing untouched.
    /// <para>
    /// Determined by asking the capability itself: one that is available now but <i>not</i>
    /// against an empty context is, by definition, gated on something the context supplies. That
    /// avoids a second declaration for implementations to keep in sync with their own predicate.
    /// </para>
    /// </summary>
    private static bool IsContextGated(IConversationCapability capability, TurnContext context) =>
        capability.IsAvailable(context) &&
        !capability.IsAvailable(TurnContext.Empty(context.UserId, context.UserChatId) with
        {
            GrantedPermissions = context.GrantedPermissions,
            SubscriptionTier = context.SubscriptionTier,
        });

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0d;
        }

        double dot = 0d, magnitudeA = 0d, magnitudeB = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA == 0d || magnitudeB == 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
