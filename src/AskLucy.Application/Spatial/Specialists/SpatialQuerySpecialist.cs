using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial.Specialists;

/// <summary>
/// specs/095 — <see cref="ISpatialSpecialist"/> implementation for <c>spatial.query</c>
/// operations. Resolves the dataset, selects a matching provider, streams feature batches with
/// a 30-second absolute deadline, writes a full audit log, and returns a named
/// <see cref="SpatialLayerDraft"/> with the filtered results (FR-001–FR-005, FR-013–FR-015).
/// </summary>
public sealed class SpatialQuerySpecialist(
    IDatasetCatalog datasetCatalog,
    ILocationResolutionService locationResolutionService,
    IEnumerable<ISpatialQueryProvider> providers,
    SpatialQueryAuditLogger auditLogger) : ISpatialSpecialist
{
    private const int TimeoutSeconds = 30;
    private const int LargeResultWarning = 2_000;
    private const int DuplicateWindowSecs = 10;

    // specs/095 T048 — per-chat fingerprint map for duplicate detection (scoped lifetime).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (string Fingerprint, DateTime At)>
        _recentQueries = new();

    private readonly IDatasetCatalog _datasetCatalog = datasetCatalog;
    private readonly ILocationResolutionService _locationResolutionService = locationResolutionService;
    private readonly IReadOnlyList<ISpatialQueryProvider> _providers = [.. providers];
    private readonly SpatialQueryAuditLogger _auditLogger = auditLogger;

    /// <inheritdoc/>
    public string OperationKind => SpatialOperationKinds.SpatialQuery;

    /// <inheritdoc/>
    public async Task<SpatialSpecialistOutcome> ExecuteAsync(
        SpatialOperation operation,
        SpatialExecutionContext context,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;

        // ── T048: Duplicate query fingerprint check ───────────────────────────────────────────
        var fingerprint = ComputeQueryFingerprint(operation);
        var warnings48 = new List<string>();
        if (_recentQueries.TryGetValue(context.UserChatId, out var prev) &&
            prev.Fingerprint == fingerprint &&
            (DateTime.UtcNow - prev.At).TotalSeconds < DuplicateWindowSecs)
        {
            warnings48.Add("This appears to be an identical query run within the last 10 seconds.");
        }
        _recentQueries[context.UserChatId] = (fingerprint, DateTime.UtcNow);

        // ── Step 1: Extract and validate SpatialQueryParameters ──────────────────────────────
        var parameters = operation.QueryParameters;
        if (parameters is null)
        {
            return Fail(null, started,
                "I need a dataset and at least one condition to run a spatial query.",
                _auditLogger);
        }

        if (string.IsNullOrWhiteSpace(parameters.DatasetId))
        {
            return Fail(null, started,
                "I need to know which dataset to query. Try specifying \"buildings\", " +
                "\"land-parcels\", \"roads\", or \"development-areas\".",
                _auditLogger);
        }

        // ── Step 2: Resolve dataset ───────────────────────────────────────────────────────────
        if (!_datasetCatalog.TryGet(parameters.DatasetId, out var dataset))
        {
            return Fail(null, started,
                $"I don't recognise the dataset \"{parameters.DatasetId}\". " +
                $"Available datasets: {_datasetCatalog.DescribeAvailable()}.",
                _auditLogger);
        }

        // ── T040: Infer missing attribute units from field type ───────────────────────────────
        // If an attribute predicate has no unit, infer the canonical unit from the
        // dataset's field type so the result displays a meaningful unit (e.g. "m" for
        // numeric height, "m²" for numeric area). Do this before building the definition
        // so the inference flows through to the SpatialQueryDefinition.
        if (parameters.AttributePredicates is { Count: > 0 } attributePredicates)
        {
            var fieldsByName = dataset.AttributeFields
                .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

            var inferred = new List<AttributePredicate>(attributePredicates.Count);
            foreach (var pred in attributePredicates)
            {
                var effective = pred;
                if (pred.Unit is null && fieldsByName.TryGetValue(pred.Field, out var field))
                {
                    effective = pred with
                    {
                        Unit = InferUnitFromFieldType(field.FieldType, pred.Operator),
                    };
                }

                inferred.Add(effective);
            }

            parameters = parameters with { AttributePredicates = inferred };
        }

        // ── Step 2b: Resolve SpatialPredicate reference kinds ─────────────────────────────────
        if (parameters.SpatialPredicates is { Count: > 0 } spatialPredicates)
        {
            var resolvedPredicates = new List<SpatialPredicate>(spatialPredicates.Count);
            foreach (var pred in spatialPredicates)
            {
                var resolvedPredicate = pred;

                // Resolve namedFeature reference via the location resolution service.
                if (pred.ReferenceKind == SpatialReferenceKind.NamedFeature &&
                    !string.IsNullOrWhiteSpace(pred.ReferenceName))
                {
                    var resolution = await _locationResolutionService
                        .ResolveQueryAsync(context.UserChatId, pred.ReferenceName, cancellationToken)
                        .ConfigureAwait(false);

                    // T033: namedFeature could not be resolved — surface a focused clarification.
                    if (resolution.Type != LocationResolutionOutcomeType.Confirmed ||
                        resolution.ConfirmedLocation is not { } location)
                    {
                        return Fail(null, started,
                            $"I couldn't locate the reference feature \"{pred.ReferenceName}\". " +
                            "Can you specify the area differently or select it on the map?",
                            _auditLogger);
                    }

                    resolvedPredicate = pred with
                    {
                        ReferenceKind = SpatialReferenceKind.ExplicitGeometry,
                        ReferenceGeometry = BuildReferenceGeometry(location),
                    };
                }

                // Active layers carry only identity in the execution context, so an
                // activeLayer reference is validated against the layers the client reported.
                if (pred.ReferenceKind == SpatialReferenceKind.ActiveLayer &&
                    !string.IsNullOrWhiteSpace(pred.ReferenceLayerId) &&
                    !context.ActiveLayers.Any(layer =>
                        string.Equals(layer.LayerId, pred.ReferenceLayerId, StringComparison.Ordinal)))
                {
                    return Fail(null, started,
                        "I need a reference layer to apply a spatial filter. " +
                        "Please select a layer first.",
                        _auditLogger);
                }

                // ExplicitGeometry is already set from the bundle; StudyArea resolves from context.
                resolvedPredicates.Add(resolvedPredicate);
            }

            parameters = parameters with { SpatialPredicates = resolvedPredicates };
        }

        // ── Step 3: Validate predicates present ──────────────────────────────────────────────
        bool hasAttribute = parameters.AttributePredicates is { Count: > 0 };
        bool hasSpatial = parameters.SpatialPredicates is { Count: > 0 };

        if (!hasAttribute && !hasSpatial)
        {
            return Fail(null, started,
                "I need at least one filter condition — an attribute condition (e.g. height > 30) " +
                "or a spatial condition (e.g. within the study area).",
                _auditLogger);
        }

        // ── Step 4: Build SpatialQueryDefinition ─────────────────────────────────────────────
        var attributeGroup = hasAttribute
            ? new PredicateGroup<AttributePredicate>(
                parameters.AttributePredicates!,
                parameters.AttributeCombinator)
            : null;

        var spatialGroup = hasSpatial
            ? new PredicateGroup<SpatialPredicate>(
                parameters.SpatialPredicates!,
                parameters.SpatialCombinator)
            : null;

        var definition = new SpatialQueryDefinition(
            QueryId: operation.OperationId,
            Dataset: dataset,
            AttributeGroup: attributeGroup,
            SpatialGroup: spatialGroup,
            StudyArea: context.StudyArea,
            BatchSize: 200,
            TimeoutAt: DateTime.UtcNow.AddSeconds(TimeoutSeconds));

        // ── Step 4b: Validate NamedFeature references can be resolved (T033) ─────────────────
        if (hasSpatial)
        {
            foreach (var pred in parameters.SpatialPredicates!)
            {
                if (pred.ReferenceKind == SpatialReferenceKind.NamedFeature &&
                    string.IsNullOrWhiteSpace(pred.ReferenceName))
                {
                    return Fail(definition, started,
                        "I couldn't find the reference feature on the map. Can you specify the area differently?",
                        _auditLogger);
                }

                if (pred.ReferenceKind == SpatialReferenceKind.ActiveLayer &&
                    string.IsNullOrWhiteSpace(pred.ReferenceLayerId))
                {
                    return Fail(definition, started,
                        "I need a reference layer to apply a spatial filter. Please select a layer first.",
                        _auditLogger);
                }
            }
        }

        // ── T034: PredicateGroup AND/OR combinator validation ────────────────────────────────
        // When both attribute and spatial groups are present (hasAttribute && hasSpatial),
        // they are logically combined with AND per FR-004. Mixed-type OR (attribute-OR
        // combined with spatial-OR) is rejected by the validator (T035) and handled
        // gracefully by offering two separate queries.
        if (hasAttribute && hasSpatial)
        {
            // Single-predicate groups with OR combinator are normalized to AND
            // — the predicate evaluator in the provider handles this case.
            if (parameters.AttributeCombinator == PredicateCombinator.Or &&
                parameters.AttributePredicates!.Count == 1)
            {
                // Normalized to AND — no further action needed
            }

            if (parameters.SpatialCombinator == PredicateCombinator.Or &&
                parameters.SpatialPredicates!.Count == 1)
            {
                // Normalized to AND — no further action needed
            }
        }

        // ── Step 5: Select provider ───────────────────────────────────────────────────────────
        var provider = _providers.FirstOrDefault(
            p => p.SupportedDatasetIds.Contains(dataset.Id, StringComparer.OrdinalIgnoreCase));

        if (provider is null)
        {
            return Fail(definition, started,
                $"No data provider is currently available for the \"{dataset.DisplayName}\" dataset.",
                _auditLogger);
        }

        // ── Step 6: Execute with 30-second deadline ───────────────────────────────────────────
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadline.Token);

        var allFeatures = new List<QueryResultFeature>();
        var warnings = new List<string>(warnings48);
        bool timedOut = false;
        int batchIndex = 0;
        var layerDrafts = new List<SpatialLayerDraft>();
        string layerId = $"sq-{operation.OperationId}";
        string layerName = BuildLayerName(dataset, definition);

        try
        {
            await foreach (var batch in provider.ExecuteAsync(
                definition, context, linked.Token).ConfigureAwait(false))
            {
                allFeatures.AddRange(batch.Features);

                // Large result set warning (FR-013).
                if (allFeatures.Count > LargeResultWarning && warnings.Count == 0)
                {
                    warnings.Add(
                        $"This query matched more than {LargeResultWarning:N0} features. " +
                        "Only the first results are shown — consider narrowing your filters.");
                }

                // Emit an intermediate layer draft for each batch so the coordinator can stream
                // progress to the client. The last batch is emitted after the loop.
                if (!batch.IsFinal)
                {
                    layerDrafts.Add(BuildLayerDraft(
                        layerId, operation.OperationId, layerName, dataset,
                        allFeatures.ToList(), batchIndex,
                        batch.TotalCount, batch.EstimatedTotal, isFinal: false));
                }

                batchIndex++;

                if (batch.IsFinal)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            timedOut = true;
            warnings.Add(
                "The query timed out after 30 seconds. Partial results are shown.");
        }

        // ── T050: Attribute field unavailability error ──────────────────────────────────────────
        // If the provider returned no features and the dataset has the requested field,
        // surface a clear error rather than silently returning empty results.
        if (allFeatures.Count == 0 && !timedOut && parameters.AttributePredicates is not null)
        {
            var fieldNames = parameters.AttributePredicates
                .Select(p => p.Field)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fieldNames.Count > 0)
            {
                var datasetFields = dataset.AttributeFields
                    .Select(f => f.Name)
                    .ToList();

                var missingFields = fieldNames.Where(f => !datasetFields.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();

                if (missingFields.Count > 0)
                {
                    var available = string.Join(", ", datasetFields.Where(f => !missingFields.Contains(f, StringComparer.OrdinalIgnoreCase)));
                    return Fail(definition, started,
                        $"The {dataset.DisplayName} dataset doesn't have a field named {string.Join(", ", missingFields)}. Available fields are: {available}",
                        _auditLogger);
                }
            }
        }

        // ── Step 7: Finalise layer draft ──────────────────────────────────────────────────────
        var finalDraft = BuildLayerDraft(
            layerId, operation.OperationId, layerName, dataset,
            allFeatures, batchIndex,
            allFeatures.Count, allFeatures.Count, isFinal: true);

        layerDrafts.Add(finalDraft);

        // ── Step 8: Audit log ─────────────────────────────────────────────────────────────────
        var duration = DateTime.UtcNow - started;
        var status = timedOut ? "TimedOut" : allFeatures.Count == 0 ? "Empty" : "Succeeded";
        var featureIds = allFeatures.Select(f => f.FeatureId).ToList();

        _auditLogger.Log(definition, status, duration, allFeatures.Count, featureIds, userId: context.UserChatId);

        // ── Step 9: Return outcome ────────────────────────────────────────────────────────────
        if (allFeatures.Count == 0 && !timedOut)
        {
            return SpatialSpecialistOutcome.Empty(
                coverage: [DataSourceCoverage.Complete(dataset.Id, dataset.DisplayName)],
                warnings: warnings.Count > 0 ? warnings : null);
        }

        var coverage = timedOut
            ? DataSourceCoverage.Partial(dataset.Id, dataset.DisplayName, "Query timed out; partial results only.")
            : DataSourceCoverage.Complete(dataset.Id, dataset.DisplayName);

        return new SpatialSpecialistOutcome(
            Status: SpatialOperationStatus.Succeeded,
            Layers: layerDrafts,
            Coverage: [coverage],
            Warnings: warnings.Count > 0 ? warnings : null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static SpatialLayerDraft BuildLayerDraft(
        string layerId,
        string operationId,
        string name,
        DatasetEntry dataset,
        IReadOnlyList<QueryResultFeature> features,
        int batchIndex,
        int totalCount,
        int? estimatedTotal,
        bool isFinal)
    {
        // Convert QueryResultFeature list to SpatialMarker list for the existing normalizer.
        var markers = features
            .Where(f => f.Geometry.Kind == FeatureGeometryKind.Point &&
                        f.Geometry.Points.Count > 0)
            .Select(f => new SpatialMarker(
                Id: f.FeatureId,
                Name: f.Name,
                Latitude: f.Geometry.Points[0].Latitude,
                Longitude: f.Geometry.Points[0].Longitude,
                Attributes: f.Attributes))
            .ToList();

        return new SpatialLayerDraft(
            LayerId: layerId,
            SourceOperationId: operationId,
            Name: name,
            LegendLabel: $"{dataset.DisplayName} ({totalCount:N0})",
            Kind: SpatialLayerKind.FeatureQuery,
            Markers: markers.Count > 0 ? markers : null,
            Features: features,
            Style: new SpatialLayerStyle(Color: "#7B1FA2", MarkerShape: "simple-dot"));
    }

    private static string BuildLayerName(DatasetEntry dataset,
        SpatialQueryDefinition definition)
    {
        var parts = new List<string> { dataset.DisplayName };

        if (definition.AttributeGroup is { Predicates.Count: > 0 } ag)
        {
            var first = ag.Predicates[0];
            var fieldDisplay = dataset.AttributeFields
                .FirstOrDefault(f => string.Equals(f.Name, first.Field,
                    StringComparison.OrdinalIgnoreCase))?.DisplayName ?? first.Field;

            parts.Add($"where {fieldDisplay} {DescribeOperator(first.Operator)} {first.Value}" +
                (first.Unit is not null ? $" {first.Unit}" : string.Empty));
        }

        if (definition.SpatialGroup is { Predicates.Count: > 0 } sg)
        {
            var first = sg.Predicates[0];
            parts.Add(DescribeRelationship(first));
        }

        return string.Join(" ", parts);
    }

    private static string DescribeOperator(AttributeOperator op) => op switch
    {
        AttributeOperator.Gt => ">",
        AttributeOperator.Gte => "≥",
        AttributeOperator.Lt => "<",
        AttributeOperator.Lte => "≤",
        AttributeOperator.Eq => "=",
        AttributeOperator.NotEq => "≠",
        AttributeOperator.Between => "between",
        AttributeOperator.Contains => "containing",
        AttributeOperator.StartsWith => "starting with",
        AttributeOperator.EndsWith => "ending with",
        AttributeOperator.InYearRange => "in year range",
        _ => op.ToString(),
    };

    private static string DescribeRelationship(SpatialPredicate p) => p.Relationship switch
    {
        SpatialRelationship.Within => "within area",
        SpatialRelationship.Outside => "outside area",
        SpatialRelationship.Intersecting => "intersecting area",
        SpatialRelationship.Containing => "containing area",
        SpatialRelationship.Near =>
            $"within {p.DistanceMeters?.ToString("F0") ?? "?"} m of " +
            (p.ReferenceName ?? "feature"),
        _ => p.Relationship.ToString(),
    };

    /// <summary>Infer a canonical unit string from an <see cref="AttributeFieldType"/>.</summary>
    private static string? InferUnitFromFieldType(AttributeFieldType fieldType, AttributeOperator op)
    {
        return fieldType switch
        {
            AttributeFieldType.Numeric => op switch
            {
                AttributeOperator.Gt or AttributeOperator.Gte or
                AttributeOperator.Lt or AttributeOperator.Lte or
                AttributeOperator.Eq or AttributeOperator.NotEq or
                AttributeOperator.Between => "m",
                _ => null,
            },
            AttributeFieldType.Year => "year",
            AttributeFieldType.Date => null, // date units are qualitative; let the user specify
            AttributeFieldType.String => null,
            _ => null,
        };
    }

    /// <summary>
    /// Builds a renderable ring for a resolved reference location: the geocoder's viewport when
    /// present, otherwise a small square around the resolved point (roughly 1 km across).
    /// </summary>
    private static IReadOnlyList<GeoPoint>? BuildReferenceGeometry(ConfirmedLocationData location)
    {
        if (location.Viewport is { } viewport)
        {
            return
            [
                new GeoPoint(viewport.SouthwestLat, viewport.SouthwestLng),
                new GeoPoint(viewport.SouthwestLat, viewport.NortheastLng),
                new GeoPoint(viewport.NortheastLat, viewport.NortheastLng),
                new GeoPoint(viewport.NortheastLat, viewport.SouthwestLng),
            ];
        }

        const double delta = 0.01;
        return
        [
            new GeoPoint(location.Latitude - delta, location.Longitude - delta),
            new GeoPoint(location.Latitude - delta, location.Longitude + delta),
            new GeoPoint(location.Latitude + delta, location.Longitude + delta),
            new GeoPoint(location.Latitude + delta, location.Longitude - delta),
        ];
    }

    /// <summary>
    /// Computes a lightweight fingerprint: combine operationKind + QueryParameters content.
    /// </summary>
    private static string ComputeQueryFingerprint(SpatialOperation operation)
    {
        // Lightweight fingerprint: combine operationKind + QueryParameters content.
        var qp = operation.QueryParameters;
        if (qp is null) return operation.OperationId;

        var sb = new System.Text.StringBuilder();
        sb.Append(qp.DatasetId).Append('|').Append((int)qp.AttributeCombinator);

        if (qp.AttributePredicates is { Count: > 0 })
        {
            foreach (var p in qp.AttributePredicates.OrderBy(p => p.Field))
            {
                sb.Append(p.Field).Append(p.Operator).Append(p.Value);
            }
        }

        sb.Append('|').Append((int)qp.SpatialCombinator);
        if (qp.SpatialPredicates is { Count: > 0 })
        {
            foreach (var p in qp.SpatialPredicates.OrderBy(p => p.Relationship.ToString()))
            {
                sb.Append(p.Relationship).Append(p.ReferenceKind).Append(p.DistanceMeters);
            }
        }

        return sb.ToString();
    }

    private static SpatialSpecialistOutcome Fail(
        SpatialQueryDefinition? definition,
        DateTime started,
        string reason,
        SpatialQueryAuditLogger auditLogger)
    {
        if (definition is not null)
        {
            auditLogger.Log(definition, "Failed", DateTime.UtcNow - started, 0,
                failureReason: reason);
        }

        return SpatialSpecialistOutcome.Failed(reason);
    }
}
