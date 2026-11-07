using AskLucy.Application.Spatial;

namespace AskLucy.Application.Spatial.Query;

/// <summary>
/// specs/095 — the validated, typed query produced by <c>SpatialQuerySpecialist</c> and
/// consumed by <see cref="ISpatialQueryProvider"/>. Attribute and spatial groups are always
/// combined with AND; OR may only join predicates within the same type group (clarification Q4).
/// </summary>
/// <param name="QueryId">Equals the bundle operation's <c>operationId</c>.</param>
/// <param name="Dataset">The resolved dataset entry.</param>
/// <param name="AttributeGroup">Present when at least one attribute predicate is defined.</param>
/// <param name="SpatialGroup">Present when at least one spatial predicate is defined.</param>
/// <param name="StudyArea">The resolved study area, when applicable.</param>
/// <param name="BatchSize">Maximum features per batch. Default 200.</param>
/// <param name="TimeoutAt">Absolute UTC deadline; specialist creates a CancellationToken from this.</param>
public sealed record SpatialQueryDefinition(
    string QueryId,
    DatasetEntry Dataset,
    PredicateGroup<AttributePredicate>? AttributeGroup,
    PredicateGroup<SpatialPredicate>? SpatialGroup,
    ResolvedStudyArea? StudyArea,
    int BatchSize = 200,
    DateTime? TimeoutAt = null);
