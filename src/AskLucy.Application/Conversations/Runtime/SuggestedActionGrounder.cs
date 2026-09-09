using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Domain.Conversations;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>The outcome of grounding one raw offer-step response.</summary>
/// <param name="Offer">The grounded offer, or null when nothing survived — the turn ends with no offer (FR-025), never a partial one.</param>
/// <param name="DroppedReasons">One entry per discarded row (or the whole document), for the turn record (FR-024).</param>
public sealed record SuggestedActionGroundingResult(SuggestedActionOffer? Offer, IReadOnlyList<string> DroppedReasons);

/// <summary>
/// Turns the offer step's raw JSON into a grounded <see cref="SuggestedActionOffer"/> (specs/045
/// FR-024, FR-024a, data-model.md §1, contracts/suggested-actions-api.md §0).
///
/// <para>
/// <b>Grounding differs by kind, exactly as the domain model documents it.</b> A <c>capability</c>
/// row's key and arguments are checked absolutely, the same two-stage check
/// <see cref="CapabilityExecutor"/> applies at dispatch — a key not in the turn's available set, or
/// arguments that fail its <c>InputSchemaJson</c>, drops the row. A <c>flowVariant</c> row (specs/045
/// Phase 6) is grounded by exact match against the candidates the orchestrator computed and handed
/// in — the model may only select one by its compound key, never supply its own arguments, so
/// there is nothing to validate beyond "is this key one we actually offered." A <c>followUp</c> row
/// has no key to check at all — it is validated only best-effort, for phrasing that promises
/// platform work (FR-021c, SC-002b); the real guarantee is structural, upheld by there being no
/// dispatch path from a follow-up to any capability.
/// </para>
///
/// <para>
/// <b>Per-row, then whole-offer.</b> Bad rows are dropped individually and logged with their own
/// reason (FR-024) while good ones survive. Only after that pass does the assembled result face a
/// holistic check — at least one substantive row, at most the configured cap, no duplicate
/// <c>(Kind, Key, ArgumentsJson)</c> triple. Failing that check drops the <b>entire</b> offer
/// (data-model.md §1) rather than showing, say, a lone decline row nobody asked to see.
/// </para>
///
/// <para><b>Never throws.</b> Malformed JSON, a wrong-shaped element, an unreadable field — every
/// case degrades to "nothing survived" rather than taking the turn down (FR-025's failure matrix:
/// "Grounded offer fails validation → Drop the whole offer, log").</para>
/// </summary>
public sealed class SuggestedActionGrounder(IJsonSchemaValidator schemaValidator)
{
    /// <summary>Mirrors <see cref="CapabilityExecutor"/>'s own ceiling — an offer's bound arguments are no more trusted than a dispatched capability's.</summary>
    private const long MaxArgumentBytes = 32 * 1024;

    private static readonly string[] DoingPhrases =
    [
        "i'll ", "i will ", "let me ", "i'm going to", "i am going to", "i can go ",
        "i'll search", "i'll open", "i'll run", "i'll do", "i'll fetch", "i'll pull",
        "i'll check", "i'll look up", "i'll find", "i'll get", "i'll show", "i'll move",
        "i'll focus", "i'll outline", "i'll zoom", "searching your", "opening the",
        "running the", "fetching the",
    ];

    public SuggestedActionGroundingResult Ground(
        string content, TurnContext context, ConversationCapabilityCatalog catalog, int maxSuggestedActions,
        IReadOnlyList<FlowVariantOfferCandidate>? flowVariantCandidates = null)
    {
        var flowVariants = (flowVariantCandidates ?? []).ToDictionary(c => c.CompoundKey, StringComparer.Ordinal);
        var dropped = new List<string>();
        var maxSubstantive = Math.Max(1, maxSuggestedActions - 1);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(ExtractJsonObject(content));
        }
        catch (JsonException)
        {
            dropped.Add("the offer response was not valid JSON");
            return new SuggestedActionGroundingResult(null, dropped);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("actions", out var actionsElement) ||
                actionsElement.ValueKind != JsonValueKind.Array)
            {
                dropped.Add("the offer response had no 'actions' array");
                return new SuggestedActionGroundingResult(null, dropped);
            }

            var question = root.TryGetProperty("question", out var questionElement) &&
                           questionElement.ValueKind == JsonValueKind.String &&
                           questionElement.GetString() is { Length: > 0 } questionText
                ? questionText
                : "What would you like to do next?";

            var available = catalog.AvailableFor(context).ToDictionary(c => c.Name, StringComparer.Ordinal);
            var substantive = new List<SuggestedAction>();
            // FollowUp rows carry no Key/ArgumentsJson at all — including Text in the tuple is
            // what keeps two differently-composed follow-ups distinct rather than colliding as
            // "the same row" the moment data-model.md's (Kind, Key, ArgumentsJson) triple is read
            // literally for a kind that has neither.
            var seen = new HashSet<(SuggestedActionKind Kind, string? Key, string? ArgumentsJson, string? Text)>();

            foreach (var element in actionsElement.EnumerateArray())
            {
                if (substantive.Count >= maxSubstantive)
                {
                    dropped.Add($"the offer exceeded the cap of {maxSubstantive} substantive rows");
                    break;
                }

                // ValueKind is checked before every TryGetProperty/GetString call below — that
                // method throws rather than returning false on a non-matching element, the exact
                // hostile-input hardening TurnDecisionParser needed for the decide step's own JSON.
                if (element.ValueKind != JsonValueKind.Object)
                {
                    dropped.Add($"an action row was {element.ValueKind}, not an object");
                    continue;
                }

                var candidate = GroundOne(element, available, flowVariants, dropped);
                if (candidate is null || !candidate.IsStructurallyValid())
                {
                    if (candidate is not null)
                    {
                        dropped.Add($"a '{candidate.Kind}' row failed structural validation");
                    }

                    continue;
                }

                if (!seen.Add((candidate.Kind, candidate.Key, candidate.ArgumentsJson, candidate.Text)))
                {
                    dropped.Add($"a duplicate '{candidate.Kind}' row was dropped");
                    continue;
                }

                substantive.Add(candidate);
            }

            // The whole-offer check (data-model.md §1): nothing worth showing survived, so the
            // turn ends with silence (FR-025) rather than a decline row standing alone.
            if (substantive.Count == 0)
            {
                return new SuggestedActionGroundingResult(null, dropped);
            }

            var offer = new SuggestedActionOffer(question, [.. substantive, SuggestedAction.Decline()]);
            return new SuggestedActionGroundingResult(offer, dropped);
        }
    }

    private SuggestedAction? GroundOne(
        JsonElement element, Dictionary<string, IConversationCapability> available,
        Dictionary<string, FlowVariantOfferCandidate> flowVariants, List<string> dropped)
    {
        var kindText = element.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String
            ? kindElement.GetString()
            : null;

        var label = ReadString(element, "label");
        var description = ReadString(element, "description") ?? string.Empty;

        switch (kindText?.Trim().ToLowerInvariant())
        {
            case "capability":
                {
                    var key = ReadString(element, "key");
                    if (string.IsNullOrEmpty(key))
                    {
                        dropped.Add("a capability row named no key");
                        return null;
                    }

                    if (!available.TryGetValue(key, out var capability))
                    {
                        dropped.Add($"'{key}' is not an available capability this turn");
                        return null;
                    }

                    var argumentsJson = element.TryGetProperty("arguments", out var argsElement) &&
                                        argsElement.ValueKind == JsonValueKind.Object
                        ? argsElement.GetRawText()
                        : "{}";

                    var schemaErrors = ValidateArguments(capability.InputSchemaJson, argumentsJson);
                    if (schemaErrors.Count > 0)
                    {
                        dropped.Add($"'{key}' was dropped: arguments did not satisfy its input contract ({string.Join("; ", schemaErrors)})");
                        return null;
                    }

                    return new SuggestedAction(
                        SuggestedActionKind.Capability, key, null,
                        label ?? capability.Label, Truncate(description, SuggestedAction.MaxDescriptionLength), argumentsJson);
                }

            case "flowvariant":
                {
                    var key = ReadString(element, "key");
                    if (string.IsNullOrEmpty(key))
                    {
                        dropped.Add("a flowVariant row named no key");
                        return null;
                    }

                    if (!flowVariants.TryGetValue(key, out var candidate))
                    {
                        dropped.Add($"'{key}' is not a flow variant offered this turn");
                        return null;
                    }

                    return new SuggestedAction(
                        SuggestedActionKind.FlowVariant, candidate.CompoundKey, null,
                        label ?? candidate.Label, Truncate(description.Length > 0 ? description : candidate.Description, SuggestedAction.MaxDescriptionLength),
                        candidate.ArgumentsJson);
                }

            case "followup":
                {
                    var text = ReadString(element, "text");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        dropped.Add("a followUp row carried no text");
                        return null;
                    }

                    if (ImpliesPlatformWork(text))
                    {
                        dropped.Add($"a follow-up implied platform work rather than Lucy talking: '{Truncate(text, 80)}'");
                        return null;
                    }

                    return new SuggestedAction(
                        SuggestedActionKind.FollowUp, null, Truncate(text, SuggestedAction.MaxTextLength),
                        label ?? Truncate(text, SuggestedAction.MaxLabelLength), Truncate(description, SuggestedAction.MaxDescriptionLength), null);
                }

            default:
                dropped.Add($"an action row named an unrecognised kind '{kindText}'");
                return null;
        }
    }

    private IReadOnlyList<string> ValidateArguments(string schemaJson, string argumentsJson)
    {
        try
        {
            using var schemaDocument = JsonDocument.Parse(schemaJson);
            using var instanceDocument = JsonDocument.Parse(argumentsJson);
            return schemaValidator.Validate(schemaDocument.RootElement, instanceDocument.RootElement, MaxArgumentBytes);
        }
        catch (JsonException ex)
        {
            return [$"arguments were not valid JSON: {ex.Message}"];
        }
    }

    /// <summary>
    /// Best-effort doing-phrasing check (FR-024's second half). Deliberately simple and
    /// over-inclusive rather than clever: a false positive costs one composed sentence being
    /// dropped and logged, while a false negative is the thing SC-002b bounds rather than
    /// eliminates — the structural guarantee (no dispatch path from a follow-up) is what actually
    /// keeps a miss from reaching a capability.
    /// </summary>
    private static bool ImpliesPlatformWork(string text)
    {
        var lowered = text.ToLowerInvariant();
        return DoingPhrases.Any(lowered.Contains);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{', StringComparison.Ordinal);
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }
}
