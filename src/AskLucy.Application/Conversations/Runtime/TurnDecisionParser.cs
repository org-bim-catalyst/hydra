using System.Text.Json;
using AskLucy.Application.Options;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>Why a decision could not be read, so the log can tell the causes apart.</summary>
public enum TurnDecisionParseFailure
{
    None,
    NotJson,
    UnrecognisedIntent,
    MissingSlices,
}

/// <summary>The outcome of reading one decision response.</summary>
/// <param name="Decision">Always populated — a failure yields <see cref="TurnDecision.AnswerOnly"/> rather than null.</param>
/// <param name="Failure">What went wrong, or <see cref="TurnDecisionParseFailure.None"/>.</param>
/// <param name="DroppedSliceReasons">Slices discarded during parsing, with the reason, for the turn record (FR-024).</param>
public sealed record TurnDecisionParseResult(
    TurnDecision Decision,
    TurnDecisionParseFailure Failure,
    IReadOnlyList<string> DroppedSliceReasons)
{
    public bool Succeeded => Failure == TurnDecisionParseFailure.None;
}

/// <summary>
/// Reads the decide step's response into a <see cref="TurnDecision"/> (specs/045 FR-034).
///
/// <para>
/// Pure and synchronous, deliberately: parsing is the part most likely to need adjusting as
/// prompts change, and keeping it free of the model call means a regression can be reproduced
/// from a captured string rather than by provoking a live provider.
/// </para>
///
/// <para>
/// <b>Never throws.</b> Every malformed shape degrades to <see cref="TurnDecision.AnswerOnly"/>
/// with a named failure. A decision step that cannot decide must not cost the user their answer
/// (FR-039), and answering in words is always available.
/// </para>
/// </summary>
public sealed class TurnDecisionParser(IOptions<ConversationRuntimeOptions> options)
{
    public TurnDecisionParseResult Parse(
        string content, IReadOnlySet<string> availableCapabilityKeys, IReadOnlySet<string>? availableFlowKeys = null)
    {
        var dropped = new List<string>();
        var flowKeys = availableFlowKeys ?? EmptyKeys;

        JsonDocument document;
        try
        {
            // Models routinely wrap JSON in a markdown fence or a sentence of preamble. That is
            // ordinary behaviour rather than a fault, so it is tolerated here instead of being
            // counted as a parse failure and burning the corrective retry.
            document = JsonDocument.Parse(ExtractJsonObject(content));
        }
        catch (JsonException)
        {
            return new TurnDecisionParseResult(TurnDecision.AnswerOnly, TurnDecisionParseFailure.NotJson, dropped);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("intent", out var intentElement) ||
                intentElement.ValueKind != JsonValueKind.String ||
                intentElement.GetString() is not { } intentText ||
                !TryParseIntent(intentText, out var intent))
            {
                return new TurnDecisionParseResult(
                    TurnDecision.AnswerOnly, TurnDecisionParseFailure.UnrecognisedIntent, dropped);
            }

            // specs/045 Phase 6 — a flowKey the model named, checked against this turn's own
            // available flows (FR-014's grounding rule, applied to flows the same as capabilities).
            // Read regardless of intent: TurnIntent.Suggest carries it forward as a hint for the
            // offer step (FR-051a.2) rather than running anything.
            var flowKey = ReadFlowKey(root, flowKeys, dropped);
            var flowArgumentsJson = flowKey is not null && root.TryGetProperty("flowArguments", out var flowArgsElement) &&
                                     flowArgsElement.ValueKind == JsonValueKind.Object
                ? flowArgsElement.GetRawText()
                : "{}";

            // "answer" and "suggest" both mean nothing runs this turn. A model that supplies
            // slices anyway is contradicting itself; the intent wins, because it is the field the
            // prompt's guidance is written against.
            if (intent != TurnIntent.Act)
            {
                return new TurnDecisionParseResult(
                    new TurnDecision(intent, [], flowKey, flowKey is null ? null : flowArgumentsJson), TurnDecisionParseFailure.None, dropped);
            }

            if (flowKey is not null)
            {
                // A flow decision replaces slices entirely (FR-050) — one job, not a job plus a
                // stray capability. A model that supplies both is contradicting itself; the flow
                // wins, both because it was named first in the document and because running a
                // partial job alongside an unrelated capability is not a coherent turn.
                if (root.TryGetProperty("slices", out var strayEl) && strayEl.ValueKind == JsonValueKind.Array && strayEl.GetArrayLength() > 0)
                {
                    dropped.Add($"a flow decision for '{flowKey}' also named slices; the slices were ignored");
                }

                return new TurnDecisionParseResult(
                    new TurnDecision(TurnIntent.Act, [], flowKey, flowArgumentsJson, ReadThroughStepIndex(root)),
                    TurnDecisionParseFailure.None, dropped);
            }

            if (!root.TryGetProperty("slices", out var slicesElement) || slicesElement.ValueKind != JsonValueKind.Array)
            {
                return new TurnDecisionParseResult(
                    TurnDecision.AnswerOnly, TurnDecisionParseFailure.MissingSlices, dropped);
            }

            var slices = new List<TurnSlice>();
            var maxSlices = options.Value.MaxCapabilityInvocationsPerTurn;

            foreach (var sliceElement in slicesElement.EnumerateArray())
            {
                if (slices.Count >= maxSlices)
                {
                    dropped.Add($"exceeded the per-turn cap of {maxSlices} capability invocations");
                    break;
                }

                // Checked before any TryGetProperty call: that method throws rather than returning
                // false when the element is not an object, so `"slices":[null]` — which a model
                // does produce — would take the whole turn down instead of dropping one slice.
                if (sliceElement.ValueKind != JsonValueKind.Object)
                {
                    dropped.Add($"a slice was {sliceElement.ValueKind}, not an object");
                    continue;
                }

                if (!sliceElement.TryGetProperty("capabilityKey", out var keyElement) ||
                    keyElement.ValueKind != JsonValueKind.String ||
                    keyElement.GetString() is not { Length: > 0 } key)
                {
                    dropped.Add("a slice named no capability");
                    continue;
                }

                // FR-014 — the model may only name what it was shown. This is the first of two
                // grounding checks; the second validates arguments against the Tier 3 schema the
                // model never saw.
                if (!availableCapabilityKeys.Contains(key))
                {
                    dropped.Add($"'{key}' is not an available capability this turn");
                    continue;
                }

                var argumentsJson = sliceElement.TryGetProperty("arguments", out var argsElement) &&
                                    argsElement.ValueKind == JsonValueKind.Object
                    ? argsElement.GetRawText()
                    : "{}";

                var pendingLabel = sliceElement.TryGetProperty("pendingLabel", out var labelElement) &&
                                   labelElement.ValueKind == JsonValueKind.String
                    ? labelElement.GetString()
                    : null;

                slices.Add(new TurnSlice(key, argumentsJson, Truncate(pendingLabel, 60), ReadDependsOn(sliceElement, slices.Count, dropped)));
            }

            // Every slice dropped while claiming to act leaves nothing to do. Degrading to a plain
            // answer beats emitting an empty "act" turn, which would show the user an
            // acknowledgement for work that never happens (FR-024 note).
            return slices.Count == 0
                ? new TurnDecisionParseResult(TurnDecision.AnswerOnly, TurnDecisionParseFailure.None, dropped)
                : new TurnDecisionParseResult(new TurnDecision(TurnIntent.Act, slices), TurnDecisionParseFailure.None, dropped);
        }
    }

    private static readonly HashSet<string> EmptyKeys = new(StringComparer.Ordinal);

    /// <summary>FR-014's grounding rule, applied to a flow key exactly as it already is to a capability key.</summary>
    private static string? ReadFlowKey(JsonElement root, IReadOnlySet<string> availableFlowKeys, List<string> dropped)
    {
        if (!root.TryGetProperty("flowKey", out var flowKeyElement) ||
            flowKeyElement.ValueKind != JsonValueKind.String ||
            flowKeyElement.GetString() is not { Length: > 0 } key)
        {
            return null;
        }

        if (!availableFlowKeys.Contains(key))
        {
            dropped.Add($"'{key}' is not an available flow this turn");
            return null;
        }

        return key;
    }

    /// <summary>FR-058's scoping — "just find it" names how far into the flow to run. Null (absent, wrong-typed, or negative) means run every step.</summary>
    private static int? ReadThroughStepIndex(JsonElement root) =>
        root.TryGetProperty("throughStepIndex", out var element) &&
        element.ValueKind == JsonValueKind.Number &&
        element.TryGetInt32(out var value) &&
        value >= 0
            ? value
            : null;

    /// <summary>
    /// Reads a dependency index, rejecting anything that is not a strictly earlier slice. A
    /// forward or self reference would deadlock the runner, so it is dropped to null and the
    /// slice simply runs independently.
    /// </summary>
    private static int? ReadDependsOn(JsonElement sliceElement, int currentIndex, List<string> dropped)
    {
        if (!sliceElement.TryGetProperty("dependsOn", out var dependsElement) ||
            dependsElement.ValueKind is not JsonValueKind.Number ||
            !dependsElement.TryGetInt32(out var dependsOn))
        {
            return null;
        }

        if (dependsOn < 0 || dependsOn >= currentIndex)
        {
            dropped.Add($"slice {currentIndex} declared dependsOn={dependsOn}, which is not an earlier slice");
            return null;
        }

        return dependsOn;
    }

    private static bool TryParseIntent(string text, out TurnIntent intent)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "answer": intent = TurnIntent.Answer; return true;
            case "act": intent = TurnIntent.Act; return true;
            case "suggest": intent = TurnIntent.Suggest; return true;
            default: intent = TurnIntent.Answer; return false;
        }
    }

    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{', StringComparison.Ordinal);
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
