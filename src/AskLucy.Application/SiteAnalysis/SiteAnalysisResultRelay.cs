using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteAnalysis;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.SiteAnalysis;

internal static partial class SiteAnalysisResultRelayLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Site analysis {SiteAnalysisId} not found for a {AnalysisType} report — dropping")]
    public static partial void AnalysisNotFound(ILogger logger, Guid siteAnalysisId, SiteAnalysisType analysisType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Specialist {AnalysisType} result for analysis {SiteAnalysisId} rejected: {Reason}")]
    public static partial void ResultRejected(ILogger logger, Guid siteAnalysisId, SiteAnalysisType analysisType, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Specialist {AnalysisType} failed for analysis {SiteAnalysisId} ({SiteName}, user {UserId}): {Reason}")]
    public static partial void ResultFailed(ILogger logger, Guid siteAnalysisId, SiteAnalysisType analysisType, string siteName, string userId, string reason, Exception? cause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Site analysis {SiteAnalysisId} lost the closing-outcome claim to a concurrent report — conceding")]
    public static partial void ClosingOutcomeConcurrencyLost(ILogger logger, Guid siteAnalysisId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifying the user about site analysis {SiteAnalysisId} failed; the result/outcome is still persisted and retrievable")]
    public static partial void NotificationFailed(ILogger logger, Guid siteAnalysisId, Exception exception);
}

/// <summary>
/// contracts/result-relay.md — the coordinating agent. Validate, persist, deliver (FR-006);
/// exactly one closing outcome per analysis (FR-025-FR-027). Every public method call is expected
/// to run inside its own, freshly-scoped <see cref="IUnitOfWork"/>/<c>DbContext</c> — see
/// <see cref="ScopeIsolatedSiteAnalysisResultRelay"/>, which is what every caller actually
/// resolves; concurrent workflow branches share one scoped <c>DbContext</c>
/// (<c>WorkflowExecutionOrchestrator.ExecuteParallelAsync</c>'s own doc comment: "neither is
/// thread-safe"), so this class must never be registered/resolved directly into a concurrent
/// branch's shared scope.
/// </summary>
public sealed class SiteAnalysisResultRelay(
    ISiteAnalysisRepository repository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    ISiteAnalysisNotifier notifier,
    IPanelNotifier panelNotifier,
    SiteAnalysisContentComposer composer,
    ILogger<SiteAnalysisResultRelay> logger) : ISiteAnalysisResultRelay
{
    private const string Actor = "system:site-analysis-relay";

    /// <summary>data-model.md — which analysis types declare a file output; used by the validation gate's "file reference" check.</summary>
    private static readonly HashSet<SiteAnalysisType> AnalysisTypesRequiringDocument = [SiteAnalysisType.SchematicImage];

    public async Task ReportSuccessAsync(
        Guid siteAnalysisId,
        SiteAnalysisType analysisType,
        SiteAnalysisResultMetadata metadata,
        JsonDocument content,
        Guid? documentId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await repository.GetByIdAsync(siteAnalysisId, cancellationToken);
        if (analysis is null)
        {
            SiteAnalysisResultRelayLog.AnalysisNotFound(logger, siteAnalysisId, analysisType);
            return;
        }

        var rejectionReason = Validate(analysis, analysisType, metadata, content, documentId);
        if (rejectionReason is not null)
        {
            analysis.AddFailedResult(analysisType, rejectionReason, wasRejected: true, Actor);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            SiteAnalysisResultRelayLog.ResultRejected(logger, siteAnalysisId, analysisType, rejectionReason);
            await SettleIfCompleteAsync(analysis, cancellationToken);
            return;
        }

        var contentJson = content.RootElement.GetRawText();
        var result = analysis.AddResult(analysisType, contentJson, metadata.DataSource, metadata.ConfidenceLevel, documentId, Actor);

        // FR-008 — the notice is a real, persisted assistant message, not only a transient
        // SignalR payload: a page reload must still show it via the conversation's normal message
        // history, exactly like every other assistant turn. Added to the SAME SaveChanges call as
        // the result itself, so the two are never observably out of step.
        var noticeText = BuildResultNoticeText(analysisType);
        messageRepository.Add(Message.Create(analysis.UserChatId, MessageRole.Assistant, MessageKind.Text, noticeText, sourceText: null, Actor));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await TryNotifyAsync(
            () => notifier.ResultReceivedAsync(
                analysis.UserId,
                new SiteAnalysisResultReceivedDto(analysis.Id, result.Id, analysis.UserChatId, analysisType.ToString(), noticeText, metadata.GeneratedAtUtc),
                cancellationToken),
            analysis.Id,
            cancellationToken);

        await TryNotifyAsync(
            () => panelNotifier.PanelRequestedAsync(
                analysis.UserId,
                PanelRequestDto.ForContent(result.Id.ToString(), BuildPanelTitle(analysisType, analysis.SiteName), content.RootElement.Clone()),
                cancellationToken),
            analysis.Id,
            cancellationToken);

        await SettleIfCompleteAsync(analysis, cancellationToken);
    }

    public async Task ReportFailureAsync(
        Guid siteAnalysisId,
        SiteAnalysisType analysisType,
        string failureReason,
        Exception? cause,
        CancellationToken cancellationToken = default)
    {
        var analysis = await repository.GetByIdAsync(siteAnalysisId, cancellationToken);
        if (analysis is null)
        {
            SiteAnalysisResultRelayLog.AnalysisNotFound(logger, siteAnalysisId, analysisType);
            return;
        }

        // FR-022/FR-022a — captured here (persisted with the originating detail) and logged with
        // structured context, never swallowed. FR-024 — deliberately delivers nothing to the user;
        // constitution §2 VIII governs capture/diagnosability, not user disclosure (research.md D13).
        analysis.AddFailedResult(analysisType, failureReason, wasRejected: false, Actor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        SiteAnalysisResultRelayLog.ResultFailed(logger, siteAnalysisId, analysisType, analysis.SiteName, analysis.UserId, failureReason, cause);

        await SettleIfCompleteAsync(analysis, cancellationToken);
    }

    /// <summary>contracts/result-relay.md § Validation gate. Returns a rejection reason, or null if valid.</summary>
    private static string? Validate(
        Domain.SiteAnalysis.SiteAnalysis analysis, SiteAnalysisType analysisType, SiteAnalysisResultMetadata metadata, JsonDocument content, Guid? documentId)
    {
        if (analysis.Status != SiteAnalysisStatus.Running)
        {
            return $"Analysis is no longer running (status: {analysis.Status}).";
        }

        if (analysis.Results.Any(r => r.AnalysisType == analysisType))
        {
            return $"Specialist '{analysisType}' has already reported for this analysis.";
        }

        if (string.IsNullOrWhiteSpace(metadata.DataSource))
        {
            return "A data source is required.";
        }

        var root = content.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("blocks", out var blocksElement) ||
            blocksElement.ValueKind != JsonValueKind.Array ||
            blocksElement.GetArrayLength() == 0)
        {
            return "Content must be a document carrying at least one block.";
        }

        foreach (var block in blocksElement.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
            {
                return "Every content block must be an object carrying a 'kind' string.";
            }
        }

        if (AnalysisTypesRequiringDocument.Contains(analysisType) && documentId is null)
        {
            return $"Specialist '{analysisType}' must report a persisted document.";
        }

        return null;
    }

    /// <summary>If every specialist has now settled, atomically claims and delivers the single closing outcome (FR-025-FR-027).</summary>
    private async Task SettleIfCompleteAsync(Domain.SiteAnalysis.SiteAnalysis analysis, CancellationToken cancellationToken)
    {
        if (analysis.Results.Count < analysis.ExpectedResultCount)
        {
            return;
        }

        if (!analysis.TryClaimClosingOutcome(Actor))
        {
            return;
        }

        var succeededCount = analysis.Results.Count(r => r.Status == SiteAnalysisResultStatus.Completed);
        if (succeededCount > 0)
        {
            analysis.Complete(Actor);
        }
        else
        {
            analysis.Fail(Actor);
        }

        // FR-008/FR-025-FR-027 — same reasoning as the per-result notice: persisted as a real
        // message (when there is one to show at all — FR-024's quiet stays when noticeText is
        // null) in the same commit as the analysis's terminal-state transition.
        var noticeText = BuildClosingNoticeText(analysis.Status, succeededCount, analysis.ExpectedResultCount);
        if (noticeText is not null)
        {
            messageRepository.Add(Message.Create(analysis.UserChatId, MessageRole.Assistant, MessageKind.Text, noticeText, sourceText: null, Actor));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A sibling report's own SettleIfCompleteAsync call raced ours between the in-memory
            // TryClaimClosingOutcome check and this SaveChanges — their write already advanced
            // RowVersion and ours was rejected as a concurrency conflict. Caught here as the
            // generic Exception, not the EF Core-specific DbUpdateConcurrencyException, which
            // Application must never reference directly per constitution §3
            // (AgentExecutionOrchestrator's own doc comment states this same rule for the
            // identical reason). They won the claim; concede rather than retry ours (constitution §5).
            SiteAnalysisResultRelayLog.ClosingOutcomeConcurrencyLost(logger, analysis.Id);
            return;
        }

        await TryNotifyAsync(
            () => notifier.AnalysisCompletedAsync(
                analysis.UserId,
                new SiteAnalysisCompletedDto(analysis.Id, analysis.UserChatId, analysis.Status.ToString(), succeededCount, analysis.ExpectedResultCount, noticeText),
                cancellationToken),
            analysis.Id,
            cancellationToken);
    }

    /// <summary>contracts/site-analysis-hub-events.md "SiteAnalysisCompleted" — null when everything succeeded (FR-024's quiet is preserved); copy only when something failed.</summary>
    private static string? BuildClosingNoticeText(SiteAnalysisStatus status, int succeededCount, int expectedCount)
    {
        if (status == SiteAnalysisStatus.Failed)
        {
            return "I couldn't complete the site analysis.";
        }

        return succeededCount == expectedCount
            ? null
            : $"Site analysis finished — {succeededCount} of {expectedCount} completed.";
    }

    private static string BuildResultNoticeText(SiteAnalysisType analysisType) =>
        $"Receiving the {FormatAnalysisTypeName(analysisType)}…";

    private static string BuildPanelTitle(SiteAnalysisType analysisType, string siteName) =>
        $"{FormatAnalysisTypeName(analysisType)} — {siteName}";

    private static string FormatAnalysisTypeName(SiteAnalysisType analysisType) => analysisType switch
    {
        SiteAnalysisType.SchematicImage => "schematic site map",
        _ => analysisType.ToString(),
    };

    /// <summary>A push failure must never roll back the persisted result (contracts/result-relay.md) — logged, never rethrown.</summary>
    private async Task TryNotifyAsync(Func<Task> notify, Guid analysisId, CancellationToken cancellationToken)
    {
        try
        {
            await notify();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SiteAnalysisResultRelayLog.NotificationFailed(logger, analysisId, ex);
        }
    }
}
