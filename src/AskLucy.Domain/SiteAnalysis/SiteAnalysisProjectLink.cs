using AskLucy.Domain.Common;

namespace AskLucy.Domain.SiteAnalysis;

/// <summary>How a <see cref="SiteAnalysisProjectLink"/> was established (spec.md FR-024, Key Entity "Project Link").</summary>
public enum SiteAnalysisProjectLinkSource
{
    InboundDeepLink,
    BootstrapCreated,
    BootstrapMatched,
}

/// <summary>
/// The association between one Ask Lucy <c>UserChat</c> and one TheDigitalCore Project
/// (data-model.md). A pure reference — never a copy of TheDigitalCore's own Project/Company/
/// Attachment data (FR-025); <see cref="TheDigitalCoreProjectId"/> is treated as an opaque
/// external identifier.
///
/// <para>No domain event is raised on creation. Per constitution &#167;3 this codebase's actual
/// established convention (discovered while implementing this feature — see <c>Project.cs</c>'s
/// own doc comment for the same discovery on spec 018) is a direct call within the same owning
/// Application handler/transaction, not a dispatched domain event, for a reaction with exactly one
/// consumer in the same bounded context — there is no cross-context subscriber for "a link was
/// created" today.</para>
/// </summary>
public sealed class SiteAnalysisProjectLink : BaseEntity
{
    public Guid UserChatId { get; private set; }

    public string TheDigitalCoreProjectId { get; private set; } = string.Empty;

    public SiteAnalysisProjectLinkSource LinkSource { get; private set; }

    public string SiteName { get; private set; } = string.Empty;

    public decimal? ResolvedLatitude { get; private set; }

    public decimal? ResolvedLongitude { get; private set; }

    private SiteAnalysisProjectLink()
    {
        // Required by EF Core materialization.
    }

    public static SiteAnalysisProjectLink Create(
        Guid userChatId,
        string theDigitalCoreProjectId,
        SiteAnalysisProjectLinkSource linkSource,
        string siteName,
        decimal? resolvedLatitude,
        decimal? resolvedLongitude)
    {
        if (userChatId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A SiteAnalysisProjectLink must reference a real UserChat.");
        }

        if (string.IsNullOrWhiteSpace(theDigitalCoreProjectId))
        {
            throw new DomainRuleViolationException("A SiteAnalysisProjectLink must reference a real TheDigitalCore project id.");
        }

        if (string.IsNullOrWhiteSpace(siteName))
        {
            throw new DomainRuleViolationException("A SiteAnalysisProjectLink must record the site name it was established for.");
        }

        return new SiteAnalysisProjectLink
        {
            Id = Guid.CreateVersion7(),
            UserChatId = userChatId,
            TheDigitalCoreProjectId = theDigitalCoreProjectId,
            LinkSource = linkSource,
            SiteName = siteName,
            ResolvedLatitude = resolvedLatitude,
            ResolvedLongitude = resolvedLongitude,
        };
    }
}
