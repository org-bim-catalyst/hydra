using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Conversations.Runtime;

internal static partial class SubAgentDelegatorLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Turn for chat {UserChatId} named {SliceCount} delegations, above the {MaxDelegations} cap; the rest were dropped (FR-002)")]
    public static partial void CapExceeded(ILogger logger, Guid userChatId, int sliceCount, int maxDelegations);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Turn for chat {UserChatId} named the same delegation ({CapabilityKey}, identical arguments) more than once; the repeat was dropped (FR-019)")]
    public static partial void DuplicateDropped(ILogger logger, Guid userChatId, string capabilityKey);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Slice for chat {UserChatId} named capability {CapabilityKey}, but it could not be resolved within its own sub-agent area")]
    public static partial void CapabilityMissingInArea(ILogger logger, Guid userChatId, string capabilityKey);
}

/// <summary>One delegated slice's outcome (specs/045 FR-016-FR-019, T094), for the turn record.</summary>
public sealed record SubAgentDelegationResult(string CapabilityKey, bool Succeeded, string? ResultJson);

/// <summary>
/// Runs a decided turn's slices as sub-agent delegations (specs/045 US5, FR-016-FR-019, T094).
///
/// <para>
/// <b>Waves, not a flat loop.</b> Slices with no <see cref="TurnSlice.DependsOn"/> run
/// concurrently as one wave; a slice that depends on another waits for its dependency's wave to
/// finish first. <see cref="TurnDecisionParser"/> already guarantees <c>DependsOn</c> can only
/// name a strictly earlier slice, so waves can never deadlock or cycle.
/// </para>
///
/// <para>
/// <b>Isolation</b> (FR-016, research.md D12 — this repo has shipped the shared-DbContext bug
/// before). Every slice, concurrent or not, runs inside its own freshly created
/// <see cref="IServiceScope"/> and resolves its own scoped <see cref="ConversationCapabilityCatalog"/>
/// and <see cref="CapabilityExecutor"/> from it — never the request's own scope — so two slices
/// racing in the same wave can never touch the same scoped <c>DbContext</c>. A single slice pays
/// the same isolation cost as a concurrent one rather than special-casing "this wave happened to
/// have one member," which would make correctness depend on how many slices a given turn happens
/// to name.
/// </para>
///
/// <para>
/// <b>Passing a result forward rather than re-deriving it</b> (FR-017). The decide step fixes
/// every slice's arguments in one shot before anything runs, so a dependent slice cannot itself
/// know its dependency's real output in advance — exactly the situation <see
/// cref="Flows.LocateAPlaceFlow"/> solves by hand for its own two fixed steps, copying step 1's
/// <c>latitude</c>/<c>longitude</c>/<c>locationName</c> straight into step 3's arguments by name.
/// This generalises that same convention rather than inventing a new one: once a dependency
/// succeeds, its result JSON's own top-level properties are merged into the dependent slice's
/// arguments — filling in only what the dependent did not itself already specify — before it
/// runs. A dependency pair whose capabilities do not already share property names by convention
/// has nothing to merge and the dependent runs with only what the decide step gave it, same as
/// before this feature existed; a dependency that failed produces no JSON result to merge at all,
/// so the dependent runs unmerged and typically fails its own schema check, which is already a
/// clear, correctly narrated outcome (FR-018) — no separate cascading-skip concept is needed.
/// </para>
///
/// <para>
/// <b>Partial failure</b> (FR-018) needs no separate reporting step: each slice narrates its own
/// outcome exactly as a standalone act-path slice always has (<see cref="CapabilityNarrator"/>'s
/// existing failure wording), so a wave of two where one fails already tells the user, in its own
/// message, which succeeded and which did not — nothing here suppresses or merges those.
/// </para>
/// </summary>
public sealed class SubAgentDelegator(
    IServiceScopeFactory scopeFactory,
    ConversationCapabilityCatalog capabilityCatalog,
    CapabilityNarrator narrator,
    IOptions<ConversationRuntimeOptions> options,
    ILogger<SubAgentDelegator> logger)
{
    public async IAsyncEnumerable<ChatStreamChunk> RunAsync(
        ConversationTurnRequest request,
        IReadOnlyList<TurnSlice> slices,
        TurnContext turnContext,
        List<SubAgentDelegationResult> record,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var accepted = AcceptSlices(slices, request.ChatId);
        if (accepted.Count == 0)
        {
            yield break;
        }

        var completed = new Dictionary<int, SubAgentDelegationResult>();
        var remaining = new List<int>(accepted.Keys);

        while (remaining.Count > 0)
        {
            var wave = remaining.Where(i => accepted[i].DependsOn is not { } dep || completed.ContainsKey(dep)).ToList();
            remaining = [.. remaining.Except(wave)];

            var channel = Channel.CreateUnbounded<ChatStreamChunk>();
            var waveTasks = wave
                .Select(i => RunOneDelegationAsync(request, i, ResolveArguments(accepted[i], completed), turnContext, channel.Writer, cancellationToken))
                .ToList();
            var pump = PumpAsync(waveTasks, channel.Writer);

            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }

            await pump;

            foreach (var task in waveTasks)
            {
                var (index, outcome) = await task;
                completed[index] = outcome;
            }
        }

        record.AddRange(accepted.Keys.Select(i => completed[i]));
    }

    private static async Task PumpAsync(List<Task<(int Index, SubAgentDelegationResult Outcome)>> waveTasks, ChannelWriter<ChatStreamChunk> writer)
    {
        try
        {
            await Task.WhenAll(waveTasks);
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// FR-002/FR-019 — the two loop-protection rules a free-form, model-named delegation graph
    /// needs that a single capability slice never did. <see cref="Agents.Runtime.AgentBudgetGuard"/>
    /// and <see cref="Agents.Runtime.AgentDuplicateToolCallDetector"/> are the platform's existing
    /// guards for exactly this, but both operate over persisted <see
    /// cref="Domain.Agents.AgentExecutionPolicy"/>/<see cref="Domain.Agents.AgentToolCall"/> rows
    /// that a conversational turn does not have until Phase 8's <c>TurnRecorder</c> lands (same
    /// deferral as T040) — so the cap is enforced directly against <see
    /// cref="ConversationRuntimeOptions.MaxDelegationsPerTurn"/>, and "repeated identical
    /// delegation" is checked the same way that guard checks it (exact capability key + exact
    /// argument JSON, ordinal), just against this turn's own slice list instead of a persisted
    /// call history.
    /// </summary>
    private Dictionary<int, TurnSlice> AcceptSlices(IReadOnlyList<TurnSlice> slices, Guid chatId)
    {
        var maxDelegations = options.Value.MaxDelegationsPerTurn;
        var accepted = new Dictionary<int, TurnSlice>();
        var seen = new HashSet<(string CapabilityKey, string ArgumentsJson)>();
        var capLogged = false;

        for (var i = 0; i < slices.Count; i++)
        {
            if (accepted.Count >= maxDelegations)
            {
                if (!capLogged)
                {
                    SubAgentDelegatorLog.CapExceeded(logger, chatId, slices.Count, maxDelegations);
                    capLogged = true;
                }

                break;
            }

            var slice = slices[i];
            if (!seen.Add((slice.CapabilityKey, slice.ArgumentsJson)))
            {
                SubAgentDelegatorLog.DuplicateDropped(logger, chatId, slice.CapabilityKey);
                continue;
            }

            // A dependency the cap or the duplicate check just dropped can never complete, so this
            // slice would wait forever. Degrade to independent instead — the same graceful
            // fallback TurnDecisionParser itself applies to a structurally invalid index.
            var dependsOn = slice.DependsOn is { } dep && accepted.ContainsKey(dep) ? dep : (int?)null;
            accepted[i] = slice with { DependsOn = dependsOn };
        }

        return accepted;
    }

    /// <summary>FR-017 — see the class doc's "Passing a result forward" section for why this merges by property name rather than mutating <see cref="TurnContext"/>.</summary>
    private static TurnSlice ResolveArguments(TurnSlice slice, Dictionary<int, SubAgentDelegationResult> completed)
    {
        if (slice.DependsOn is not { } dep ||
            !completed.TryGetValue(dep, out var dependency) ||
            !dependency.Succeeded ||
            dependency.ResultJson is not { } dependencyResultJson)
        {
            return slice;
        }

        return slice with { ArgumentsJson = MergeArgumentsWithDependency(slice.ArgumentsJson, dependencyResultJson) };
    }

    private static string MergeArgumentsWithDependency(string ownArgumentsJson, string dependencyResultJson)
    {
        try
        {
            using var dependencyDocument = JsonDocument.Parse(dependencyResultJson);
            using var ownDocument = JsonDocument.Parse(ownArgumentsJson);

            if (dependencyDocument.RootElement.ValueKind != JsonValueKind.Object ||
                ownDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ownArgumentsJson;
            }

            // The dependency's own fields go in first; the dependent's own explicit arguments are
            // applied second so they always win a name collision — the decide step's own
            // judgement on that particular argument is never overridden by an inference.
            var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in dependencyDocument.RootElement.EnumerateObject())
            {
                merged[property.Name] = property.Value.Clone();
            }

            foreach (var property in ownDocument.RootElement.EnumerateObject())
            {
                merged[property.Name] = property.Value.Clone();
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var (name, value) in merged)
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // Either side was not a JSON object — degrade to the dependent's own arguments
            // unmerged rather than failing the slice before it even runs.
            return ownArgumentsJson;
        }
    }

    private async Task<(int Index, SubAgentDelegationResult Outcome)> RunOneDelegationAsync(
        ConversationTurnRequest request,
        int sliceIndex,
        TurnSlice slice,
        TurnContext turnContext,
        ChannelWriter<ChatStreamChunk> writer,
        CancellationToken cancellationToken)
    {
        var area = capabilityCatalog.Find(slice.CapabilityKey)?.Area;
        if (area is null)
        {
            SubAgentDelegatorLog.CapabilityMissingInArea(logger, request.ChatId, slice.CapabilityKey);
            await writer.WriteAsync(new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: null), cancellationToken);
            await writer.WriteAsync(new ChatStreamChunk("That's no longer available — could you try again?", null), cancellationToken);
            return (sliceIndex, new SubAgentDelegationResult(slice.CapabilityKey, false, null));
        }

        // Fresh scope per slice, whether or not this wave has company — a slice's isolation must
        // not depend on how many other slices happen to run alongside it this turn (research.md
        // D12).
        using var scope = scopeFactory.CreateScope();
        var capability = scope.ServiceProvider.GetRequiredService<ConversationCapabilityCatalog>().FindInArea(area.Value, slice.CapabilityKey);

        if (capability is null)
        {
            SubAgentDelegatorLog.CapabilityMissingInArea(logger, request.ChatId, slice.CapabilityKey);
            await writer.WriteAsync(new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: null), cancellationToken);
            await writer.WriteAsync(new ChatStreamChunk("That's no longer available — could you try again?", null), cancellationToken);
            return (sliceIndex, new SubAgentDelegationResult(slice.CapabilityKey, false, null));
        }

        var scopedExecutor = scope.ServiceProvider.GetRequiredService<CapabilityExecutor>();

        await writer.WriteAsync(new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: slice.PendingLabel ?? capability.Label), cancellationToken);

        var result = await scopedExecutor.ExecuteAsync(capability, turnContext, slice.ArgumentsJson, cancellationToken);
        var narration = await narrator.NarrateAsync(request, capability, result, nextStepLabel: null, cancellationToken);
        await writer.WriteAsync(new ChatStreamChunk(narration, null), cancellationToken);

        if (result.Succeeded)
        {
            var structured = StructuredPayloadExtractor.TryExtract(capability.Name, result.ResultJson);
            if (structured is not null)
            {
                await writer.WriteAsync(structured, cancellationToken);
            }
        }

        return (sliceIndex, new SubAgentDelegationResult(capability.Name, result.Succeeded, result.Succeeded ? result.ResultJson : null));
    }
}
