namespace AskLucy.Domain.Ai;

/// <summary>
/// A distinct job the platform asks an LLM to do, independently of whose conversation it is.
/// <para>
/// Every member here previously chose its provider by falling through
/// <c>DefaultProviderResolver</c>'s last resort — "first enabled provider in display-name
/// order" — or, for <see cref="BoundaryVision"/>, by a hardcoded provider key. Neither is a
/// decision an administrator ever made, which is how location intent classification came to run
/// on a provider whose credit had run out while the operator's own chat ran happily on another.
/// </para>
/// <para>
/// <see cref="Chat"/> joined them once the chat default moved out of per-user Settings and into
/// the admin panel: which model answers a user is a platform decision like the rest, so it is
/// configured the same way rather than through a second mechanism.
/// </para>
/// </summary>
public enum AiCapability
{
    /// <summary>Answers the user in conversation. Every other member is background work.</summary>
    Chat,

    /// <summary>Decides whether a message asks to view a place, and extracts the place name.</summary>
    LocationIntent,

    /// <summary>Reads a finished conversation for durable facts worth remembering.</summary>
    MemoryExtraction,

    /// <summary>Decides whether a new memory contradicts one already stored.</summary>
    MemoryConflictDetection,

    /// <summary>Detects an uploaded document's language and classifies it.</summary>
    DocumentClassification,

    /// <summary>Cross-checks a candidate site boundary against satellite imagery.</summary>
    BoundaryVision,

    /// <summary>
    /// Decides what a chat turn needs — whether to act at all, which capability or flow, and
    /// whether to run it or offer it (specs/045-conversational-agent-runtime, FR-037).
    /// <para>
    /// Appended last so no persisted <c>AiCapabilityAssignment</c> integer shifts. Supersedes
    /// <see cref="LocationIntent"/> for chat turns: the decide step answers the same question and
    /// more, so running both would classify the same message twice (research.md D11). It is short,
    /// structured and on the critical path, so an administrator will usually want it on a fast,
    /// cheap model — which is exactly what a per-capability assignment is for.
    /// </para>
    /// </summary>
    TurnOrchestration,
}
