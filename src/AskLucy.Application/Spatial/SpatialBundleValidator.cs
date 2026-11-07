using AskLucy.Application.Spatial.Query;
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

    private readonly IDatasetCatalog? _datasetCatalog;

    /// <summary>Parameterless constructor — dataset-aware rules are skipped.</summary>
    public SpatialBundleValidator() { ConfigureRules(); }

    /// <summary>
    /// specs/095 — constructor with <see cref="IDatasetCatalog"/> for full <c>spatial.query</c>
    /// parameter validation (field names, operator compatibility, spatial relationships).
    /// </summary>
    public SpatialBundleValidator(IDatasetCatalog datasetCatalog)
    {
        _datasetCatalog = datasetCatalog;
        ConfigureRules();
    }

    private void ConfigureRules()
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

        // ── specs/095 spatial.query parameter rules ───────────────────────────────────────────
        RuleForEach(bundle => bundle.Operations)
            .ChildRules(operation =>
            {
                operation
                    .RuleFor(o => o.QueryParameters)
                    .Must(HaveValidSpatialQueryParameters)
                    .When(o => string.Equals(
                        o.OperationKind,
                        SpatialOperationKinds.SpatialQuery,
                        StringComparison.Ordinal))
                    .WithMessage(
                        "A spatial.query operation must include a datasetId and at least one " +
                        "attribute or spatial predicate.");

                operation
                    .RuleFor(o => o.QueryParameters)
                    .Must(p => HaveValidAttributePredicates(p, _datasetCatalog))
                    .When(o =>
                        string.Equals(o.OperationKind, SpatialOperationKinds.SpatialQuery,
                            StringComparison.Ordinal) &&
                        o.QueryParameters?.AttributePredicates is { Count: > 0 } &&
                        _datasetCatalog is not null)
                    .WithMessage(
                        "One or more attribute predicates reference fields that do not exist on " +
                        "the specified dataset, use an operator incompatible with the field type, " +
                        "or are missing required values.");

                operation
                    .RuleFor(o => o.QueryParameters)
                    .Must(p => HaveValidSpatialPredicates(p, _datasetCatalog))
                    .When(o =>
                        string.Equals(o.OperationKind, SpatialOperationKinds.SpatialQuery,
                            StringComparison.Ordinal) &&
                        o.QueryParameters?.SpatialPredicates is { Count: > 0 } &&
                        _datasetCatalog is not null)
                    .WithMessage(
                        "One or more spatial predicates use a relationship not supported by the " +
                        "dataset, or are missing a required distance when using 'Near'.");
            });

        // ── specs/095 T035: mixed-type OR rejection ──────────────────────────────────────────
        RuleForEach(bundle => bundle.Operations)
            .ChildRules(operation =>
            {
                operation
                    .RuleFor(o => o.QueryParameters)
                    .Must(NotHaveMixedTypeOr)
                    .When(o =>
                        string.Equals(o.OperationKind, SpatialOperationKinds.SpatialQuery,
                            StringComparison.Ordinal) &&
                        o.QueryParameters?.AttributePredicates is { Count: > 0 } &&
                        o.QueryParameters?.SpatialPredicates  is { Count: > 0 })
                    .WithMessage(
                        "I can combine attribute and spatial conditions with AND, but not OR across types. " +
                        "Would you like me to run two separate queries instead?");
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

    // ── specs/095 spatial.query helpers ──────────────────────────────────────────────────────

    private static bool HaveValidSpatialQueryParameters(SpatialQueryParameters? qp)
    {
        if (qp is null || string.IsNullOrWhiteSpace(qp.DatasetId))
        {
            return false;
        }

        return (qp.AttributePredicates is { Count: > 0 }) ||
               (qp.SpatialPredicates   is { Count: > 0 });
    }

    private static bool HaveValidAttributePredicates(
        SpatialQueryParameters? qp,
        IDatasetCatalog? catalog)
    {
        if (qp is null ||
            catalog is null ||
            qp.AttributePredicates is not { Count: > 0 })
        {
            return true; // nothing to validate
        }

        if (!catalog.TryGet(qp.DatasetId, out var dataset))
        {
            return true; // dataset unknown — specialist will surface the error
        }

        var fieldsByName = dataset.AttributeFields
            .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var predicate in qp.AttributePredicates)
        {
            if (!fieldsByName.TryGetValue(predicate.Field, out var field))
            {
                return false; // unknown field
            }

            if (!IsOperatorValidForType(predicate.Operator, field.FieldType))
            {
                return false; // operator/type mismatch
            }

            // Between and InYearRange require a ValueTo.
            if (predicate.Operator is AttributeOperator.Between or AttributeOperator.InYearRange &&
                predicate.ValueTo is null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveValidSpatialPredicates(
        SpatialQueryParameters? qp,
        IDatasetCatalog? catalog)
    {
        if (qp is null ||
            catalog is null ||
            qp.SpatialPredicates is not { Count: > 0 })
        {
            return true;
        }

        if (!catalog.TryGet(qp.DatasetId, out var dataset))
        {
            return true;
        }

        foreach (var predicate in qp.SpatialPredicates)
        {
            if (!dataset.SupportedRelationships.Contains(predicate.Relationship))
            {
                return false;
            }

            if (predicate.Relationship == SpatialRelationship.Near &&
                predicate.DistanceMeters is not > 0)
            {
                return false;
            }

            // ── T030: reference kind presence validation ──────────────────────────────────────
            if (predicate.ReferenceKind == SpatialReferenceKind.NamedFeature &&
                string.IsNullOrWhiteSpace(predicate.ReferenceName))
            {
                return false; // ReferenceName required for NamedFeature
            }

            if (predicate.ReferenceKind == SpatialReferenceKind.ActiveLayer &&
                string.IsNullOrWhiteSpace(predicate.ReferenceLayerId))
            {
                return false; // ReferenceLayerId required for ActiveLayer
            }

            if (predicate.ReferenceKind == SpatialReferenceKind.ExplicitGeometry &&
                predicate.ReferenceGeometry is null)
            {
                return false; // ReferenceGeometry required for ExplicitGeometry
            }
        }

        return true;
    }

    // ── specs/095 T035: mixed-type OR ────────────────────────────────────────────────────────
    private static bool NotHaveMixedTypeOr(SpatialQueryParameters? qp)
    {
        if (qp is null) return true;

        // Mixed-type OR is disallowed: attribute-OR and spatial-OR at the same time.
        // Within a single type group, OR is fine.
        bool attributeIsOr = qp.AttributeCombinator == PredicateCombinator.Or &&
                             qp.AttributePredicates is { Count: > 1 };
        bool spatialIsOr   = qp.SpatialCombinator   == PredicateCombinator.Or &&
                             qp.SpatialPredicates   is { Count: > 1 };

        // If both groups use OR it creates cross-type OR ambiguity — reject.
        return !(attributeIsOr && spatialIsOr);
    }

    private static bool IsOperatorValidForType(AttributeOperator op, AttributeFieldType type)
    {
        return type switch
        {
            AttributeFieldType.Numeric => op is
                AttributeOperator.Gt or AttributeOperator.Gte or
                AttributeOperator.Lt or AttributeOperator.Lte or
                AttributeOperator.Eq or AttributeOperator.NotEq or
                AttributeOperator.Between,

            AttributeFieldType.Year => op is
                AttributeOperator.Eq or AttributeOperator.NotEq or
                AttributeOperator.Gt or AttributeOperator.Gte or
                AttributeOperator.Lt or AttributeOperator.Lte or
                AttributeOperator.InYearRange or AttributeOperator.Between,

            AttributeFieldType.Date => op is
                AttributeOperator.Eq or AttributeOperator.NotEq or
                AttributeOperator.Gt or AttributeOperator.Gte or
                AttributeOperator.Lt or AttributeOperator.Lte or
                AttributeOperator.Between,

            AttributeFieldType.String => op is
                AttributeOperator.Eq or AttributeOperator.NotEq or
                AttributeOperator.Contains or AttributeOperator.StartsWith or
                AttributeOperator.EndsWith,

            _ => false,
        };
    }
}
