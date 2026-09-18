using AskLucy.Domain.Common;

namespace AskLucy.Domain.SiteAnalysis;

/// <summary>
/// Aggregate root for the Site Analysis bounded context (specs/057-site-analysis-agent
/// data-model.md). One request to analyze one site, by one user, within one conversation. Owns
/// its <see cref="SiteAnalysisResult"/> children — reachable only through this aggregate's
/// repository, never their own <c>DbSet</c> (constitution &#167;5). The relay resolves which chat
/// to notify from <see cref="UserChatId"/> here, never from a workflow tool execution context,
/// because <c>AgentNodeExecutor</c>-style nested executions null that link out (research.md D2).
/// </summary>
public sealed class SiteAnalysis : BaseEntity
{
    private readonly List<SiteAnalysisResult> _results = [];

    public string UserId { get; private set; } = string.Empty;

    public Guid UserChatId { get; private set; }

    public string SiteName { get; private set; } = string.Empty;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    /// <summary>Snapshot of the confirmed boundary when one was resolved; null when only a point was confirmed. Deliberately frozen — a later boundary re-resolution must not retroactively change what an analysis was run against.</summary>
    public string? BoundaryGeoJson { get; private set; }

    public SiteAnalysisStatus Status { get; private set; }

    /// <summary>Diagnostic link to the fan-out execution; null until dispatch succeeds.</summary>
    public Guid? WorkflowExecutionId { get; private set; }

    /// <summary>How many specialists were dispatched — lets the closing outcome (FR-025) be computed without re-reading the workflow definition.</summary>
    public int ExpectedResultCount { get; private set; }

    /// <summary>
    /// Set the moment the single closing outcome (FR-025-FR-027) is claimed. Its presence — not a
    /// separate flag — is what enforces "exactly once" under concurrent branch completions: the
    /// caller that transitions this from null wins the claim (see <see cref="TryClaimClosingOutcome"/>).
    /// </summary>
    public DateTime? ClosingOutcomeReportedAtUtc { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public IReadOnlyCollection<SiteAnalysisResult> Results => _results;

    private SiteAnalysis()
    {
        // Required by EF Core materialization.
    }

    public static SiteAnalysis Create(
        string userId, Guid userChatId, string siteName, double latitude, double longitude,
        string? boundaryGeoJson, int expectedResultCount, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A site analysis must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(siteName))
        {
            throw new DomainRuleViolationException("A site analysis must name a site.");
        }

        if (expectedResultCount < 1)
        {
            throw new DomainRuleViolationException("A site analysis must dispatch at least one specialist.");
        }

        return new SiteAnalysis
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            UserChatId = userChatId,
            SiteName = siteName.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            BoundaryGeoJson = boundaryGeoJson,
            Status = SiteAnalysisStatus.Running,
            ExpectedResultCount = expectedResultCount,
            StartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Records a specialist's validated success (contracts/result-relay.md). Enforces the unique-per-(analysis, type) invariant — a specialist reports once.</summary>
    public SiteAnalysisResult AddResult(
        SiteAnalysisType analysisType, string contentJson, string dataSource,
        SiteAnalysisConfidenceLevel confidenceLevel, Guid? documentId, string actor)
    {
        EnsureNotAlreadySettled(analysisType);

        var result = SiteAnalysisResult.Completed(Id, analysisType, contentJson, dataSource, confidenceLevel, documentId, actor);
        _results.Add(result);
        return result;
    }

    /// <summary>Records a specialist's failure or a rejected result (contracts/result-relay.md § Behavior on failure).</summary>
    public SiteAnalysisResult AddFailedResult(SiteAnalysisType analysisType, string failureReason, bool wasRejected, string actor)
    {
        EnsureNotAlreadySettled(analysisType);

        var result = wasRejected
            ? SiteAnalysisResult.Rejected(Id, analysisType, failureReason, actor)
            : SiteAnalysisResult.Failed(Id, analysisType, failureReason, actor);
        _results.Add(result);
        return result;
    }

    private void EnsureNotAlreadySettled(SiteAnalysisType analysisType)
    {
        if (_results.Any(r => r.AnalysisType == analysisType))
        {
            throw new DomainRuleViolationException($"Specialist '{analysisType}' has already reported for this analysis.");
        }
    }

    /// <summary>
    /// Atomically claims the right to emit the single closing outcome (FR-027). Returns whether
    /// <b>this</b> caller won the claim — relies on <see cref="BaseEntity.RowVersion"/> optimistic
    /// concurrency at the persistence layer: a caller that loses a race sees its
    /// <c>SaveChanges</c> throw <c>DbUpdateConcurrencyException</c>, which the relay handles by
    /// re-reading and conceding (constitution &#167;5) rather than retrying this method.
    /// </summary>
    public bool TryClaimClosingOutcome(string actor)
    {
        if (ClosingOutcomeReportedAtUtc is not null)
        {
            return false;
        }

        ClosingOutcomeReportedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
        return true;
    }

    public void Complete(string actor)
    {
        Status = SiteAnalysisStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Fail(string actor)
    {
        Status = SiteAnalysisStatus.Failed;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
