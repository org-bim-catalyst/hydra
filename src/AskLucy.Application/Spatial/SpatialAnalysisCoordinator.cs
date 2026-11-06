using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Spatial;

internal static partial class SpatialAnalysisCoordinatorLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Spatial request {BundleId} for chat {UserChatId} was invalid: {Reason}")]
    public static partial void InvalidRequest(ILogger logger, string bundleId, Guid userChatId, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Spatial operation {OperationId} ({OperationKind}) failed for chat {UserChatId} via {Specialist}: {Reason}")]
    public static partial void OperationFailed(
        ILogger logger, string operationId, string operationKind, Guid userChatId, string? specialist, string? reason);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Spatial specialist {Specialist} failed for operation {OperationId} ({OperationKind})")]
    public static partial void SpecialistThrew(
        ILogger logger, string specialist, string operationId, string operationKind, Exception exception);
}

/// <summary>What the model asked for, before it becomes a versioned bundle.</summary>
public sealed record SpatialRequestInput(
    IReadOnlyList<SpatialOperationInput> Operations,
    SpatialLayerMode Mode = SpatialLayerMode.Ask);

/// <summary>One operation as the deciding model expressed it.</summary>
public sealed record SpatialOperationInput(
    string Kind,
    string? Category = null,
    string? Area = null,
    string? TargetCategory = null,
    double? Distance = null,
    string? Unit = null,
    bool? Beyond = null,
    IReadOnlyList<SpatialBandInput>? Bands = null);

/// <summary>One requested distance band, in the unit the request also carries.</summary>
public sealed record SpatialBandInput(double Lower, double Upper);

/// <summary>
/// What one request produced: a normalized result, a clarification question, or a failure reason
/// for a request that could not even be executed. Exactly one is set.
/// </summary>
public sealed record SpatialCoordinatorOutcome(
    SpatialAnalysisResult? Result = null,
    string? Question = null,
    string? AreaName = null,
    SpatialLayerMode Mode = SpatialLayerMode.Replace,
    string? FailureReason = null,
    SpatialPendingQuestion? PendingQuestion = null);

/// <summary>
/// The coordinator (FR-001/FR-002): validates an interpreted request, resolves where each operation
/// runs, dispatches independent operations concurrently to registered specialists, isolates their
/// failures, and merges everything into one normalized result.
/// </summary>
public sealed class SpatialAnalysisCoordinator(
    SpatialSpecialistRegistry registry,
    SpatialBundleValidator validator,
    SpatialResultNormalizer normalizer,
    StudyAreaResolver studyAreaResolver,
    SpatialLayerStateStore layerStateStore,
    TimeProvider timeProvider,
    ILogger<SpatialAnalysisCoordinator> logger)
{
    /// <summary>Cap on concurrent provider work; the free spatial providers saturate under load.</summary>
    public const int MaxParallelOperations = 4;

    public async Task<SpatialCoordinatorOutcome> ExecuteAsync(
        SpatialRequestInput input,
        SpatialExecutionContext baseContext,
        string? lastUserMessage,
        CancellationToken cancellationToken)
    {
        var analysisId = Guid.CreateVersion7().ToString("N");
        var bundleId = Guid.CreateVersion7().ToString("N");
        var chatId = baseContext.UserChatId;

        if (input.Operations.Count == 0)
        {
            return new SpatialCoordinatorOutcome(
                FailureReason: "I couldn't tell what to look for on the map.");
        }

        var mode = ResolveLayerMode(input, chatId, lastUserMessage);
        if (mode.ImmediateQuestion is { } keepOrReplace)
        {
            return new SpatialCoordinatorOutcome(
                Question: keepOrReplace,
                Mode: SpatialLayerMode.Ask,
                PendingQuestion: SpatialPendingQuestion.KeepOrReplace);
        }

        var bundle = BuildBundle(bundleId, input, baseContext.MapContext);
        var validation = await validator.ValidateAsync(bundle, cancellationToken);
        if (!validation.IsValid)
        {
            var reason = string.Join("; ", validation.Errors.Select(error => error.ErrorMessage));
            SpatialAnalysisCoordinatorLog.InvalidRequest(logger, bundle.BundleId, chatId, reason);

            return new SpatialCoordinatorOutcome(FailureReason: reason, Mode: mode.Mode);
        }

        // Resolve where each operation runs before dispatching anything: a missing area is a
        // follow-up question, not a per-operation failure (FR-005).
        var areas = new Dictionary<string, StudyAreaResolution>(StringComparer.Ordinal);
        foreach (var operation in bundle.Operations)
        {
            var resolution = await studyAreaResolver.ResolveAsync(
                chatId, operation.StudyArea, baseContext.MapContext, cancellationToken);

            if (resolution.ClarificationQuestion is { Length: > 0 } question)
            {
                layerStateStore.SetPending(chatId, new PendingSpatialRequest(
                    operation.OperationKind,
                    input.Operations.Count > 0 ? input.Operations[0].Category : null,
                    input.Operations.Count > 0 ? input.Operations[0].Area : null,
                    SpatialPendingQuestion.WhichArea,
                    timeProvider.GetUtcNow().UtcDateTime));

                return new SpatialCoordinatorOutcome(
                    Question: question,
                    Mode: mode.Mode,
                    PendingQuestion: SpatialPendingQuestion.WhichArea);
            }

            areas[operation.OperationId] = resolution;
        }

        var executions = await DispatchAsync(bundle, baseContext, areas, cancellationToken);
        var result = normalizer.Normalize(analysisId, bundle.BundleId, executions);

        if (result.HasLayers)
        {
            layerStateStore.ApplyLayers(chatId, analysisId, result.Layers, keepExisting: mode.Mode == SpatialLayerMode.Keep);
        }
        else if (mode.Mode == SpatialLayerMode.Replace && result.Status != SpatialAnalysisStatus.Failed)
        {
            // Replace with nothing to draw still honours the user's choice: the old layers go.
            layerStateStore.ApplyLayers(chatId, analysisId, [], keepExisting: false);
        }

        foreach (var failed in result.Operations.Where(o => o.Status == SpatialOperationStatus.Failed))
        {
            SpatialAnalysisCoordinatorLog.OperationFailed(
                logger, failed.OperationId, failed.OperationKind, chatId, failed.SpecialistName, failed.FailureReason);
        }

        var areaName = areas.Values
            .Select(resolution => resolution.Area?.DisplayName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        return new SpatialCoordinatorOutcome(Result: result, AreaName: areaName, Mode: mode.Mode);
    }

    private LayerModeResolution ResolveLayerMode(
        SpatialRequestInput input,
        Guid chatId,
        string? lastUserMessage)
    {
        if (input.Mode != SpatialLayerMode.Ask)
        {
            return new LayerModeResolution(input.Mode, null);
        }

        var pending = layerStateStore.GetPending(chatId);
        if (pending?.Question == SpatialPendingQuestion.KeepOrReplace)
        {
            // A bare answer to the clear-or-keep question carries no mode of its own. "keep" is
            // explicit; anything else means replace — the same rule spec 090 established.
            var keep = lastUserMessage?.Contains("keep", StringComparison.OrdinalIgnoreCase) == true;
            return new LayerModeResolution(keep ? SpatialLayerMode.Keep : SpatialLayerMode.Replace, null);
        }

        var existingLayers = layerStateStore.GetLayers(chatId);
        if (existingLayers.Count > 0)
        {
            var existingNames = string.Join(", ", existingLayers
                .Select(layer => layer.LegendLabel.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase));

            var requested = input.Operations.Count > 0 && !string.IsNullOrWhiteSpace(input.Operations[0].Category)
                ? input.Operations[0].Category!.ToLowerInvariant()
                : "the new results";

            var question =
                $"You already have {existingNames} on the map. Should I clear those layers and show " +
                $"{requested} instead, or keep them alongside the new results?";

            layerStateStore.SetPending(chatId, new PendingSpatialRequest(
                input.Operations.Count > 0 ? input.Operations[0].Kind : string.Empty,
                input.Operations.Count > 0 ? input.Operations[0].Category : null,
                input.Operations.Count > 0 ? input.Operations[0].Area : null,
                SpatialPendingQuestion.KeepOrReplace,
                timeProvider.GetUtcNow().UtcDateTime));

            return new LayerModeResolution(SpatialLayerMode.Ask, question);
        }

        return new LayerModeResolution(SpatialLayerMode.Replace, null);
    }

    private static SpatialRequestBundle BuildBundle(
        string bundleId,
        SpatialRequestInput input,
        MapViewportContext? mapContext)
    {
        var operations = input.Operations
            .Select((operation, index) => BuildOperation(operation, index))
            .ToList();

        return SpatialRequestBundle.Create(
            bundleId,
            operations,
            new SpatialPreferences(input.Mode),
            mapContext);
    }

    private static SpatialOperation BuildOperation(SpatialOperationInput input, int index)
    {
        var operationId = $"op-{index + 1}";
        var studyArea = string.IsNullOrWhiteSpace(input.Area)
            ? null
            : new StudyAreaReference(StudyAreaKind.NamedArea, NamedArea: input.Area);

        if (string.Equals(input.Kind, SpatialOperationKinds.ProximityAnalysis, StringComparison.OrdinalIgnoreCase))
        {
            var targets = new List<FeatureSelector>();
            if (!string.IsNullOrWhiteSpace(input.Category))
            {
                targets.Add(new FeatureSelector(Category: input.Category, Role: "source"));
            }

            if (!string.IsNullOrWhiteSpace(input.TargetCategory))
            {
                targets.Add(new FeatureSelector(Category: input.TargetCategory, Role: "target"));
            }

            var unit = input.Unit ?? "km";
            var thresholds = input.Bands is { Count: > 0 } bands
                ? bands
                    .Select(band => new SpatialDistanceThreshold(band.Lower, band.Upper))
                    .ToList()
                : input.Distance is { } distance && double.IsFinite(distance) && distance > 0
                    ? [new SpatialDistanceThreshold(
                        0,
                        distance,
                        input.Beyond == true ? $"more than {distance} {unit}" : $"within {distance} {unit}")]
                    : null;

            var parameters = thresholds is { Count: > 0 }
                ? new SpatialOperationParameters(
                    thresholds,
                    unit,
                    input.Bands is { Count: > 1 } ? null : input.Beyond == true ? "beyond" : "within")
                : null;

            return new SpatialOperation(operationId, SpatialOperationKinds.ProximityAnalysis, targets, parameters, studyArea);
        }

        if (string.Equals(input.Kind, SpatialOperationKinds.PoiDiscovery, StringComparison.OrdinalIgnoreCase))
        {
            return new SpatialOperation(
                operationId,
                SpatialOperationKinds.PoiDiscovery,
                [new FeatureSelector(Category: input.Category)],
                null,
                studyArea);
        }

        // Any other kind is carried through unchanged: the registry decides whether it can run,
        // and adding a specialist must not require editing this builder (FR-028).
        return new SpatialOperation(
            operationId,
            input.Kind,
            [new FeatureSelector(Category: input.Category)],
            null,
            studyArea);
    }

    private async Task<IReadOnlyList<SpatialSpecialistExecution>> DispatchAsync(
        SpatialRequestBundle bundle,
        SpatialExecutionContext baseContext,
        Dictionary<string, StudyAreaResolution> areas,
        CancellationToken cancellationToken)
    {
        var completed = new Dictionary<string, SpatialSpecialistExecution>(StringComparer.Ordinal);
        var remaining = bundle.Operations.ToList();

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(operation => (operation.DependsOn ?? []).All(completed.ContainsKey))
                .ToList();

            if (ready.Count == 0)
            {
                // The validator rejects cycles; this is a defensive end rather than an infinite loop.
                foreach (var operation in remaining)
                {
                    completed[operation.OperationId] = new SpatialSpecialistExecution(
                        operation, "none", SpatialSpecialistOutcome.Failed("The operation's dependencies could not be satisfied."));
                }

                break;
            }

            using var semaphore = new SemaphoreSlim(MaxParallelOperations);
            var tasks = ready.Select(operation => RunOperationAsync(
                operation, baseContext, areas[operation.OperationId], semaphore, cancellationToken));

            var results = await Task.WhenAll(tasks);
            foreach (var execution in results)
            {
                completed[execution.Operation.OperationId] = execution;
            }

            remaining.RemoveAll(ready.Contains);
        }

        return [.. bundle.Operations.Select(operation => completed[operation.OperationId])];
    }

    private async Task<SpatialSpecialistExecution> RunOperationAsync(
        SpatialOperation operation,
        SpatialExecutionContext baseContext,
        StudyAreaResolution areaResolution,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGet(operation.OperationKind, out var specialist))
        {
            return new SpatialSpecialistExecution(
                operation,
                "none",
                SpatialSpecialistOutcome.Failed(
                    $"No specialist is registered for '{operation.OperationKind}'."));
        }

        if (areaResolution.Area is not { } area)
        {
            return new SpatialSpecialistExecution(
                operation,
                specialist.GetType().Name,
                SpatialSpecialistOutcome.Failed(
                    areaResolution.FailureReason ?? "The study area could not be resolved."));
        }

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var context = baseContext with { StudyArea = area };
            var outcome = await specialist.ExecuteAsync(operation, context, cancellationToken);
            return new SpatialSpecialistExecution(operation, specialist.GetType().Name, outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SpatialAnalysisCoordinatorLog.SpecialistThrew(
                logger, specialist.GetType().Name, operation.OperationId, operation.OperationKind, ex);

            return new SpatialSpecialistExecution(operation, specialist.GetType().Name, null, ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private sealed record LayerModeResolution(SpatialLayerMode Mode, string? ImmediateQuestion);
}
