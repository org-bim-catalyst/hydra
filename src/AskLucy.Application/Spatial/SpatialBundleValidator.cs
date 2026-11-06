using FluentValidation;

namespace AskLucy.Application.Spatial;

/// <summary>
/// Structural validation for an interpreted bundle (FR-003/FR-004). It deliberately does not decide
/// whether a kind is registered — a declared kind with no specialist is executed-as-not-performed
/// by the coordinator, not rejected as a malformed bundle.
/// </summary>
public sealed class SpatialBundleValidator : AbstractValidator<SpatialRequestBundle>
{
    /// <summary>Google Maps' supported zoom range; context outside it is invalid input.</summary>
    public const double MinZoom = 0;

    public const double MaxZoom = 22;

    public SpatialBundleValidator()
    {
        RuleFor(bundle => bundle.BundleVersion)
            .Equal(SpatialRequestBundle.CurrentVersion)
            .WithMessage($"The spatial request bundle version must be {SpatialRequestBundle.CurrentVersion}.");

        RuleFor(bundle => bundle.BundleId)
            .NotEmpty()
            .WithMessage("A spatial request bundle needs an identifier.");

        RuleFor(bundle => bundle.Operations)
            .NotEmpty()
            .WithMessage("A spatial request needs at least one operation.");

        RuleFor(bundle => bundle.Operations)
            .Must(HaveUniqueOperationIds)
            .WithMessage("Spatial operation identifiers must be unique within a bundle.");

        RuleFor(bundle => bundle.Operations)
            .Must(ReferenceEarlierOperationsOnly)
            .WithMessage("A spatial operation may only depend on an earlier operation in the same bundle.");

        RuleFor(bundle => bundle.Operations)
            .Must(HaveNoDependencyCycles)
            .WithMessage("Spatial operation dependencies must not form a cycle.");

        RuleForEach(bundle => bundle.Operations).ChildRules(operation =>
        {
            operation.RuleFor(o => o.OperationId)
                .NotEmpty()
                .WithMessage("Every spatial operation needs an identifier.");

            operation.RuleFor(o => o.OperationKind)
                .NotEmpty()
                .WithMessage("Every spatial operation needs a kind.");

            operation.RuleFor(o => o.Targets)
                .Must(targets => targets.Any(target => target.HasTarget))
                .WithMessage("Every spatial operation needs a category or an explicit geometry.");

            operation.RuleFor(o => o.Parameters!.DistanceThresholds)
                .Must(BePositiveAndOrdered)
                .When(o => o.Parameters?.DistanceThresholds is { Count: > 0 })
                .WithMessage("Distance thresholds must be positive, ordered and non-overlapping.");
        });

        RuleFor(bundle => bundle.MapContext)
            .Must(BeValidMapContext)
            .When(bundle => bundle.MapContext is not null)
            .WithMessage("The supplied map view is not a valid, bounded area.");
    }

    private static bool HaveUniqueOperationIds(IReadOnlyList<SpatialOperation> operations) =>
        operations.Select(o => o.OperationId).Distinct(StringComparer.Ordinal).Count() == operations.Count;

    private static bool ReferenceEarlierOperationsOnly(IReadOnlyList<SpatialOperation> operations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation.DependsOn is { Count: > 0 } dependencies &&
                dependencies.Any(dependency => !seen.Contains(dependency)))
            {
                return false;
            }

            seen.Add(operation.OperationId);
        }

        return true;
    }

    private static bool HaveNoDependencyCycles(IReadOnlyList<SpatialOperation> operations)
    {
        // Duplicate ids are already a separate error; building the map would throw first.
        if (!HaveUniqueOperationIds(operations))
        {
            return false;
        }

        var byId = operations.ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string id)
        {
            if (visited.Contains(id))
            {
                return true;
            }

            if (!visiting.Add(id))
            {
                return false;
            }

            if (byId.TryGetValue(id, out var operation) && operation.DependsOn is { Count: > 0 } dependencies &&
                dependencies.Any(dependency => !Visit(dependency)))
            {
                return false;
            }

            visiting.Remove(id);
            visited.Add(id);
            return true;
        }

        return operations.All(operation => Visit(operation.OperationId));
    }

    private static bool BePositiveAndOrdered(IReadOnlyList<SpatialDistanceThreshold>? thresholds)
    {
        if (thresholds is not { Count: > 0 })
        {
            return true;
        }

        var previousUpper = 0d;
        foreach (var threshold in thresholds)
        {
            if (!double.IsFinite(threshold.LowerBound) || threshold.LowerBound < 0 ||
                threshold.UpperBound is not { } upper || !double.IsFinite(upper) || upper <= threshold.LowerBound ||
                threshold.LowerBound < previousUpper)
            {
                return false;
            }

            previousUpper = upper;
        }

        return true;
    }

    private static bool BeValidMapContext(MapViewportContext? context)
    {
        if (context is null)
        {
            return true;
        }

        return double.IsFinite(context.CenterLatitude) && context.CenterLatitude is >= -90 and <= 90 &&
               double.IsFinite(context.CenterLongitude) && context.CenterLongitude is >= -180 and <= 180 &&
               double.IsFinite(context.Zoom) && context.Zoom is >= MinZoom and <= MaxZoom &&
               double.IsFinite(context.SouthWestLatitude) && context.SouthWestLatitude is >= -90 and <= 90 &&
               double.IsFinite(context.SouthWestLongitude) && context.SouthWestLongitude is >= -180 and <= 180 &&
               double.IsFinite(context.NorthEastLatitude) && context.NorthEastLatitude is >= -90 and <= 90 &&
               double.IsFinite(context.NorthEastLongitude) && context.NorthEastLongitude is >= -180 and <= 180 &&
               context.SouthWestLatitude < context.NorthEastLatitude &&
               context.SouthWestLongitude < context.NorthEastLongitude;
    }
}
