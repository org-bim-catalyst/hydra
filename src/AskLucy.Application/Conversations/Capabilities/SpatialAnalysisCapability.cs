using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.Spatial;
using AskLucy.Application.Viewer;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// specs/094-spatial-analysis-orchestrator — the single front door for every geographic, POI and
/// spatial-analysis request (FR-001). It interprets the decider's arguments into a
/// <see cref="SpatialRequestInput"/>, delegates to the coordinator, and turns the normalized
/// result into the one viewer command Lucy emits.
/// <para>
/// It replaces <c>FindPointsOfInterestCapability</c>: keeping both would give the decider two
/// spatial front doors and leave a second delivery path alive, which is exactly what this feature
/// removes.
/// </para>
/// </summary>
public sealed class SpatialAnalysisCapability(
    SpatialAnalysisCoordinator coordinator,
    SpatialLayerStateStore layerStateStore,
    PoiCategoryCatalog categoryCatalog,
    IMessageRepository messageRepository) : IConversationCapability
{
    public const string CapabilityKey = "spatial_analysis";

    public string Name => CapabilityKey;

    public string Description =>
        "Answers geographic and map questions: finds every place of a type in an area and draws them " +
        "as markers, or measures and highlights features within or beyond a distance. Also continues " +
        "an outstanding clear-or-keep or which-area question.";

    public string WhenToUse =>
        "Use when the user asks to see, find, show, compare or measure places on the map — " +
        "\"schools in Muscat\", \"all hospitals in Oman\", \"mosques near here\", \"schools within 1 km " +
        "of hospitals\", \"areas more than 2 km from a clinic\". Never for one named place; that is " +
        "resolve_location.";

    public string ArgumentHint =>
        "one or more operations, each with a place type and area (e.g. schools in Muscat)";

    public string UsageGuidance =>
        "Pass each requested operation in `operations`, with its `kind`, `category` (or `targetCategory`) " +
        "and `area`. Omit `area` only for \"here\"/\"this area\", which use the map view or the site " +
        "already on screen. If the result has awaitingChoice=true, ask its question and claim NOTHING " +
        "was drawn — nothing was. On \"keep both\", call again with the same operations and mode=keep; " +
        "on \"clear\"/\"replace\", mode=replace. If awaitingArea=true, ask which area to search and claim " +
        "nothing was drawn. Report the count and area used. If count is 0, say plainly that none were " +
        "found. If the search failed or was partial, say so and never claim anything was drawn that was not.";

    public string Label => "Map places and distances";

    public string OfferDescription => "Show places, compare distances and highlight areas on the map.";

    public string AcknowledgementTemplate => "Checking the map for that.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

    /// <summary>
    /// Built from <see cref="SpatialOperationKinds.Supported"/>, not a literal: registering a new
    /// specialist updates the table, and the decider sees the new kind on the very next turn
    /// without an edit here (FR-028).
    /// </summary>
    public string InputSchemaJson
    {
        get
        {
            var kinds = string.Join(
                ',',
                SpatialOperationKinds.Supported.Select(operation => JsonSerializer.Serialize(operation.OperationKind)));

            return InputSchemaTemplate.Replace("__KINDS__", kinds, StringComparison.Ordinal);
        }
    }

    private const string InputSchemaTemplate =
        """
        {"type":"object","properties":{
          "operations":{"type":"array","items":{"type":"object","properties":{
            "kind":{"type":"string","enum":[__KINDS__]},
            "category":{"type":"string","minLength":1},
            "area":{"type":"string"},
            "targetCategory":{"type":"string"},
            "distance":{"type":"number","exclusiveMinimum":0},
            "unit":{"type":"string","enum":["m","km"]},
            "beyond":{"type":"boolean"},
            "bands":{"type":"array","items":{"type":"object","properties":{
              "lower":{"type":"number","minimum":0},
              "upper":{"type":"number","exclusiveMinimum":0}
            },"required":["upper"]}}
          },"required":["kind"]}},
          "category":{"type":"string"},
          "area":{"type":"string"},
          "mode":{"type":"string","enum":["keep","replace"]}
        }}
        """;

    public string OutputSchemaJson =>
        """
        {"type":"object","properties":{
          "analysisId":{"type":"string"},"status":{"type":"string"},"mode":{"type":"string"},
          "awaitingChoice":{"type":"boolean"},"awaitingArea":{"type":"boolean"},
          "question":{"type":"string"},"count":{"type":"integer"},"areaName":{"type":"string"},
          "layers":{"type":"array"},"warnings":{"type":"array"},"coverage":{"type":"array"}
        }}
        """;

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Extended;

    public SubAgentArea Area => SubAgentArea.Spatial;

    /// <summary>
    /// Always available. A request can name its own area, and an unsupported category or a missing
    /// area is answered in words — hiding the capability would make those requests unreachable
    /// rather than better handled.
    /// </summary>
    public bool IsAvailable(TurnContext context) => true;

    /// <summary>Never offered unprompted — these are asked for, not suggested.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonDocument input,
        CancellationToken cancellationToken = default)
    {
        var chatId = context.UserChatId ?? Guid.Empty;
        var lastUserMessage = await LastUserMessageAsync(chatId, cancellationToken);
        var pending = layerStateStore.GetPending(chatId);

        var request = ParseInput(input.RootElement, lastUserMessage, pending);

        var baseContext = new SpatialExecutionContext(
            context.UserId,
            chatId,
            StudyArea: null,
            context.MapContext,
            [.. layerStateStore.GetLayers(chatId).Select(layer => new ActiveSpatialLayer(
                layer.AnalysisId, layer.LayerId, layer.Name, layer.Kind, layer.LegendLabel))]);

        var outcome = await coordinator.ExecuteAsync(request, baseContext, lastUserMessage, cancellationToken);

        if (outcome.Question is { Length: > 0 } question)
        {
            var awaitingChoice = outcome.PendingQuestion == SpatialPendingQuestion.KeepOrReplace;
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                analysisId = string.Empty,
                status = "question",
                mode = "ask",
                awaitingChoice,
                awaitingArea = outcome.PendingQuestion == SpatialPendingQuestion.WhichArea,
                question,
                count = 0,
                areaName = (string?)null,
                layers = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                coverage = Array.Empty<object>(),
            }, SpatialLayerCommand.JsonOptions));
        }

        if (outcome.Result is not { } result)
        {
            return AgentToolResult.Failure(
                outcome.FailureReason ?? "I couldn't work out what to look for on the map.");
        }

        if (result.Status == SpatialAnalysisStatus.Failed)
        {
            // A single-operation request can report the specialist's own reason (a provider
            // outage, an unresolvable area) because it is already user-facing. A multi-operation
            // failure has no single reason, so it gets the generic truth instead of a guess.
            var reason = result.Operations is [{ FailureReason: { Length: > 0 } detail }]
                ? detail
                : "I couldn't complete that spatial request. The map data could not be retrieved — " +
                  "nothing was drawn.";

            return AgentToolResult.Failure(reason);
        }

        var command = ToCommand(result, outcome);
        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(command, SpatialLayerCommand.JsonOptions));
    }

    private SpatialRequestInput ParseInput(
        JsonElement root,
        string? lastUserMessage,
        PendingSpatialRequest? pending)
    {
        var operations = new List<SpatialOperationInput>();

        if (root.TryGetProperty("operations", out var operationsElement) &&
            operationsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var operation in operationsElement.EnumerateArray())
            {
                if (operation.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var parsedCategory = ReadString(operation, "category", "placeType", "poi");
                var parsedArea = ReadString(operation, "area", "location", "city", "region", "country");

                // A bare answer to a question carries only what the user said; the pending request
                // remembers the category and area it was asked about.
                parsedCategory ??= pending?.CategoryKey;
                parsedArea ??= pending?.AreaName;

                operations.Add(new SpatialOperationInput(
                    Kind: NormalizeKind(ReadString(operation, "kind", "operationKind", "operation")),
                    Category: parsedCategory,
                    Area: parsedArea,
                    TargetCategory: ReadString(operation, "targetCategory", "target"),
                    Distance: ReadDouble(operation, "distance", "radius"),
                    Unit: ReadString(operation, "unit"),
                    Beyond: ReadBool(operation, "beyond"),
                    Bands: ReadBands(operation)));
            }
        }

        // A decider that has learned the old single-category shape, or a bare "yes"/"Oman" answer
        // to a question, must still work: fall back to one POI operation rather than failing a
        // request whose own words already say what was asked.
        if (operations.Count == 0)
        {
            var category = ReadString(root, "category", "type", "placeType", "place_type", "poi", "poiType", "place");
            var area = ReadString(root, "area", "location", "city", "region", "country");

            if (category is null && pending is not null &&
                categoryCatalog.TryResolve(pending.CategoryKey, out var pendingCategory))
            {
                category = pendingCategory.Key;
            }

            if (category is null && categoryCatalog.TryResolveFromText(lastUserMessage, out var fromText))
            {
                category = fromText.Key;
            }

            // The argument hint's own example ("schools in Muscat") is sometimes copied into
            // `area` as if it were a place. It is not one; treating it as a place used to search
            // the stale active pin instead of asking which area was meant.
            if (area is not null && categoryCatalog.TryResolveFromText(area, out _))
            {
                area = null;
            }

            operations.Add(new SpatialOperationInput(
                SpatialOperationKinds.PoiDiscovery,
                category,
                area ?? pending?.AreaName));
        }

        var mode = ReadString(root, "mode");
        var layerMode = mode switch
        {
            "keep" => SpatialLayerMode.Keep,
            "replace" => SpatialLayerMode.Replace,
            _ => SpatialLayerMode.Ask,
        };

        return new SpatialRequestInput(operations, layerMode);
    }

    private static SpatialLayerCommand ToCommand(SpatialAnalysisResult result, SpatialCoordinatorOutcome outcome) =>
        new(
            result.AnalysisId,
            StatusText(result.Status),
            outcome.Mode == SpatialLayerMode.Keep ? "keep" : "replace",
            [.. result.Layers.Select(layer => new SpatialLayerPayload(
                layer.LayerId,
                layer.SourceOperationId,
                layer.Name,
                layer.LegendLabel,
                KindText(layer.Kind),
                Markers: layer.Markers?
                    .Select(marker => new SpatialMarkerData(marker.Id, marker.Name, marker.Latitude, marker.Longitude))
                    .ToList(),
                Ring: layer.Ring?
                    .Select(point => new SpatialRingPoint(point.Latitude, point.Longitude))
                    .ToList(),
                Color: layer.Style?.Color,
                MarkerShape: layer.Style?.MarkerShape,
                Opacity: layer.Style?.Opacity,
                StrokeWidth: layer.Style?.StrokeWidth,
                Visible: layer.Visible))],
            [.. result.Warnings],
            [.. result.Coverage.Select(coverage => new SpatialCoveragePayload(
                coverage.SourceId,
                coverage.DisplayName,
                coverage.CoverageStatus,
                coverage.Explanation))],
            result.ResultCount,
            outcome.AreaName);

    private static string StatusText(SpatialAnalysisStatus status) => status switch
    {
        SpatialAnalysisStatus.Complete => "Complete",
        SpatialAnalysisStatus.Partial => "Partial",
        SpatialAnalysisStatus.Empty => "Empty",
        _ => "Failed",
    };

    private static string KindText(SpatialLayerKind kind) => kind switch
    {
        SpatialLayerKind.Markers => "markers",
        SpatialLayerKind.Buffer => "buffer",
        SpatialLayerKind.Area => "area",
        _ => "band",
    };

    /// <summary>
    /// Keeps a declared kind as written and only falls back to place discovery when the model gave
    /// none. A future specialist's kind must survive interpretation untouched (FR-028); whether it
    /// is registered is the registry's question, not the parser's.
    /// </summary>
    private static string NormalizeKind(string? kind) =>
        string.IsNullOrWhiteSpace(kind) ? SpatialOperationKinds.PoiDiscovery : kind.Trim().ToLowerInvariant();

    private async Task<string?> LastUserMessageAsync(Guid chatId, CancellationToken cancellationToken)
    {
        var messages = await messageRepository.ListByChatIdAsync(chatId, cancellationToken) ?? [];
        return messages.LastOrDefault(message => message.Role == MessageRole.User)?.Content;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var element) &&
                element.ValueKind == JsonValueKind.String &&
                element.GetString() is { Length: > 0 } value &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<SpatialBandInput>? ReadBands(JsonElement root)
    {
        if (!root.TryGetProperty("bands", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var bands = new List<SpatialBandInput>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var lower = ReadDouble(item, "lower", "from") ?? 0;
            if (ReadDouble(item, "upper", "to", "distance") is { } upper &&
                double.IsFinite(upper) && upper > lower)
            {
                bands.Add(new SpatialBandInput(lower, upper));
            }
        }

        return bands.Count > 0 ? bands : null;
    }

    private static double? ReadDouble(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number &&
                element.TryGetDouble(out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;
}
