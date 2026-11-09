using AskLucy.Application.Spatial.Query;

namespace AskLucy.Application.Spatial;

/// <summary>specs/097 — request-local overlay inputs, steps, criteria and optional score weights.</summary>
public sealed record OverlayAnalysisParameters(
    string CandidateRef,
    IReadOnlyList<OverlayInput> Inputs,
    IReadOnlyList<OverlayStep> Steps,
    IReadOnlyList<OverlayCriterion> Criteria,
    OverlayScoringProfile? Scoring = null);

public sealed record OverlayInput(string Ref, string DatasetId, OverlayInputRole Role);

public enum OverlayInputRole { Candidate, Criterion, Constraint, Reference }

public sealed record OverlayStep(
    string StepId,
    OverlayOperationKind Kind,
    IReadOnlyList<string> InputRefs,
    string OutputRef,
    OverlaySpatialRelation? Relationship = null);

public enum OverlayOperationKind { Intersection, Union, Difference, SpatialIntersection }
public enum OverlaySpatialRelation { Intersects, Within, Contains }
public enum OverlayCriterionKind { Attribute, Area, Distance, SpatialRelationship }
public enum OverlayCriterionDirection { HigherIsBetter, LowerIsBetter }

public sealed record OverlayCriterion(
    string CriterionId,
    OverlayCriterionKind Kind,
    string InputRef,
    string Operator,
    string? Field = null,
    double? Number = null,
    string? Text = null,
    string? Unit = null,
    bool Required = true,
    OverlayCriterionDirection? Direction = null);

public sealed record OverlayScoringProfile(IReadOnlyList<OverlayScoreWeight> Weights);
public sealed record OverlayScoreWeight(string CriterionId, double Weight);

/// <summary>Validated catalog references handed from Application to the Infrastructure provider.</summary>
public sealed record OverlayResolvedInput(OverlayInput Input, DatasetEntry Dataset);

public sealed record OverlayAnalysisDefinition(
    string OperationId,
    string StudyAreaName,
    ResolvedStudyArea StudyArea,
    string CandidateRef,
    IReadOnlyList<OverlayResolvedInput> Inputs,
    IReadOnlyList<OverlayStep> Steps,
    IReadOnlyList<OverlayCriterion> Criteria,
    OverlayScoringProfile? Scoring);

public sealed record OverlayAnalysisProviderResult(
    IReadOnlyList<OverlayOutputLayer> Layers,
    int CandidateCount,
    int ExcludedCount,
    IReadOnlyList<DataSourceCoverage> Coverage,
    IReadOnlyList<string> Warnings,
    string? FailureReason = null);

public sealed record OverlayOutputLayer(
    string Ref,
    string Name,
    string Role,
    string Operation,
    IReadOnlyList<QueryResultFeature> Features);

public interface IOverlayAnalysisProvider
{
    Task<OverlayAnalysisProviderResult> ExecuteAsync(
        OverlayAnalysisDefinition definition,
        SpatialExecutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>Safe, viewer-facing operation metadata for each overlay result layer.</summary>
public sealed record OverlayLayerMetadata(
    string Role,
    string Operation,
    IReadOnlyList<string> Criteria,
    IReadOnlyList<string> Sources,
    string? ScoreMeaning = null,
    IReadOnlyList<string>? Coverage = null);

public sealed record OverlayFeatureMetadata(
    IReadOnlyDictionary<string, bool> Eligibility,
    IReadOnlyDictionary<string, double> CriterionScores,
    double? Score,
    int? Rank,
    IReadOnlyList<string> Provenance);
