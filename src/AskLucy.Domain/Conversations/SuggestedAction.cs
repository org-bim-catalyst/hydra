namespace AskLucy.Domain.Conversations;

/// <summary>
/// What kind of next step a suggested-action row represents (specs/045 FR-021a).
/// <para>
/// The discriminator matters because <b>grounding differs by kind</b>. An action row names
/// something the platform will <i>do</i>, so its key is checked absolutely against the registry
/// before the row can reach a user. A follow-up row is composed prose with no key to check — it
/// is made safe structurally instead, by having no dispatch path that can reach a capability
/// (FR-021c). Collapsing these into one shape would mean either refusing to compose follow-ups
/// or pretending an unverifiable row carries the same guarantee as a verified one.
/// </para>
/// </summary>
public enum SuggestedActionKind
{
    /// <summary>Runs a declared flow prefix — "Focus and outline the site". Key is <c>flow:variant</c>.</summary>
    FlowVariant,

    /// <summary>Runs one capability. Key is the capability name.</summary>
    Capability,

    /// <summary>
    /// Runs nothing. Lucy says or asks something, using <see cref="SuggestedAction.Text"/> as her
    /// own instruction. Composed for the situation from what she has learned in comparable turns,
    /// from the need to clarify an ambiguous request, or from an obvious successor to what was
    /// just done — never selected from a fixed list (FR-021b, research.md D20).
    /// </summary>
    FollowUp,

    /// <summary>Performs no work and closes the offer. Appended server-side, always last (FR-022).</summary>
    Decline,
}

/// <summary>
/// One row of an offer (specs/045 FR-021a, data-model.md §1). Serialized as a JSON array onto the
/// assistant message that made the offer — it has exactly one owning message and is never queried
/// independently, so a child table would add a join and a lifecycle for no gain.
/// </summary>
/// <param name="Kind">Which of the four row shapes this is; decides how it is grounded and dispatched.</param>
/// <param name="Key">
/// <c>flow:variant</c> or a capability name for an action row. Null for
/// <see cref="SuggestedActionKind.FollowUp"/> and <see cref="SuggestedActionKind.Decline"/>,
/// which have nothing to resolve.
/// </param>
/// <param name="Text">The composed instruction, for a follow-up only. Null for every other kind.</param>
/// <param name="Label">User-facing, and the only part spoken aloud (FR-044).</param>
/// <param name="Description">One line beneath the label. Rendered, never spoken (FR-044).</param>
/// <param name="ArgumentsJson">Bound arguments for an action row; must satisfy the capability's input schema.</param>
public sealed record SuggestedAction(
    SuggestedActionKind Kind,
    string? Key,
    string? Text,
    string Label,
    string Description,
    string? ArgumentsJson)
{
    public const int MaxLabelLength = 80;
    public const int MaxDescriptionLength = 160;
    public const int MaxKeyLength = 120;
    public const int MaxTextLength = 300;

    /// <summary>True for the single decline row. Derived rather than stored, so the two can never disagree.</summary>
    public bool IsDecline => Kind == SuggestedActionKind.Decline;

    /// <summary>True when this row would cause the platform to do something, and therefore must be registry-checked (FR-024.1).</summary>
    public bool IsAction => Kind is SuggestedActionKind.FlowVariant or SuggestedActionKind.Capability;

    /// <summary>
    /// The decline row, composed server-side so it can never be forgotten or reworded by a model
    /// (FR-022).
    /// </summary>
    public static SuggestedAction Decline(string label = "Nothing for now") =>
        new(SuggestedActionKind.Decline, null, null, label, string.Empty, null);

    /// <summary>
    /// Structural validity — the shape rules the persisted form must satisfy. Grounding (does this
    /// key exist? is it available right now?) is a separate, runtime question and deliberately not
    /// answered here: the domain cannot see the capability registry.
    /// </summary>
    public bool IsStructurallyValid() =>
        !string.IsNullOrWhiteSpace(Label) &&
        Label.Length <= MaxLabelLength &&
        Description.Length <= MaxDescriptionLength &&
        Kind switch
        {
            SuggestedActionKind.FlowVariant or SuggestedActionKind.Capability =>
                !string.IsNullOrWhiteSpace(Key) && Key.Length <= MaxKeyLength && Text is null,
            SuggestedActionKind.FollowUp =>
                Key is null && !string.IsNullOrWhiteSpace(Text) && Text.Length <= MaxTextLength,
            SuggestedActionKind.Decline =>
                Key is null && Text is null,
            _ => false,
        };
}

/// <summary>
/// One completed offer (specs/045 FR-021, contracts/turn-stream.md §2) — the question the offer
/// step composed plus its grounded rows, always ending with the server-appended decline (FR-022).
/// <para>
/// Persisted as a whole onto <see cref="Chats.Message.SuggestedActionsJson"/> rather than the bare
/// row array data-model.md §1 describes: <see cref="Question"/> is itself composed per turn (it is
/// not a fixed string — "What would you like to do next?" versus a clarifying question read
/// differently), and SC-009 requires a reopened conversation to reproduce the exact offer that was
/// shown live. No column exists for the question text alone, and this is a strict superset of "a
/// JSON array of rows" rather than a competing shape, so storing the envelope is the smaller change.
/// </para>
/// </summary>
/// <param name="Question">What Lucy asked before the rows — spoken aloud alongside the labels (FR-044).</param>
/// <param name="Actions">Substantive rows in offer order, with the decline row always last.</param>
public sealed record SuggestedActionOffer(string Question, IReadOnlyList<SuggestedAction> Actions);
