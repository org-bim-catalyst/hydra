namespace AskLucy.Application.Spatial;

/// <summary>
/// One layer the client reports as currently drawn, sent with the next turn so the coordinator's
/// keep/replace question never contradicts the legend the user is looking at (spec 090 FR-009).
/// Only identity and labelling are trusted; the server owns styles and tag definitions.
/// </summary>
public sealed record ActiveSpatialLayer(
    string AnalysisId,
    string LayerId,
    string Name,
    string Kind,
    string LegendLabel);

/// <summary>
/// What a specialist may legitimately use. Deliberately carries no <c>TurnContext</c>: a
/// specialist must not depend on the conversation runtime or be able to deliver anything itself.
/// </summary>
/// <param name="UserId">The requesting user; provider calls run as this user.</param>
/// <param name="UserChatId">The conversation the request belongs to.</param>
/// <param name="StudyArea">The resolved, bounded study area for this operation, when applicable.</param>
/// <param name="MapContext">The validated map view supplied by the client, when present.</param>
/// <param name="ActiveLayers">What the client reports is already drawn.</param>
public sealed record SpatialExecutionContext(
    string? UserId,
    Guid UserChatId,
    ResolvedStudyArea? StudyArea,
    MapViewportContext? MapContext,
    IReadOnlyList<ActiveSpatialLayer> ActiveLayers);

/// <summary>
/// What one specialist produced. <see cref="SpatialOperationStatus.Empty"/> is a successful
/// retrieval with no matches and must never stand in for a failure (spec 093 contract).
/// </summary>
public sealed record SpatialSpecialistOutcome(
    SpatialOperationStatus Status,
    IReadOnlyList<SpatialLayerDraft> Layers,
    IReadOnlyList<DataSourceCoverage> Coverage,
    string? FailureReason = null,
    IReadOnlyList<string>? Warnings = null)
{
    public static SpatialSpecialistOutcome Empty(
        IReadOnlyList<DataSourceCoverage>? coverage = null,
        IReadOnlyList<string>? warnings = null) =>
        new(SpatialOperationStatus.Empty, [], coverage ?? [], null, warnings);

    public static SpatialSpecialistOutcome Failed(string reason, IReadOnlyList<string>? warnings = null) =>
        new(SpatialOperationStatus.Failed, [], [], reason, warnings);
}

/// <summary>
/// The only contract a spatial capability implements. One method, one operation kind; adding a
/// specialist is a new class plus a DI registration (FR-006/FR-028).
/// </summary>
public interface ISpatialSpecialist
{
    /// <summary>Stable lower-case operation kind, e.g. <c>poi.discovery</c>.</summary>
    string OperationKind { get; }

    Task<SpatialSpecialistOutcome> ExecuteAsync(
        SpatialOperation operation,
        SpatialExecutionContext context,
        CancellationToken cancellationToken);
}
