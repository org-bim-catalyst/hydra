using AskLucy.Domain.Common;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.SiteAnalysis;

public sealed class SiteAnalysisProjectLinkTests
{
    private static readonly Guid UserChatId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldSucceed_WithValidFields()
    {
        var link = SiteAnalysisProjectLink.Create(UserChatId, "tdc-project-1", SiteAnalysisProjectLinkSource.BootstrapCreated, "Al Safa Park", 25.1m, 55.2m);

        link.UserChatId.Should().Be(UserChatId);
        link.TheDigitalCoreProjectId.Should().Be("tdc-project-1");
        link.LinkSource.Should().Be(SiteAnalysisProjectLinkSource.BootstrapCreated);
        link.SiteName.Should().Be("Al Safa Park");
        link.ResolvedLatitude.Should().Be(25.1m);
        link.ResolvedLongitude.Should().Be(55.2m);
        link.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_ShouldThrow_WhenUserChatIdIsEmpty()
    {
        var act = () => SiteAnalysisProjectLink.Create(Guid.Empty, "tdc-project-1", SiteAnalysisProjectLinkSource.BootstrapMatched, "Al Safa Park", null, null);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldThrow_WhenTheDigitalCoreProjectIdIsMissing()
    {
        var act = () => SiteAnalysisProjectLink.Create(UserChatId, "  ", SiteAnalysisProjectLinkSource.BootstrapMatched, "Al Safa Park", null, null);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldThrow_WhenSiteNameIsMissing()
    {
        var act = () => SiteAnalysisProjectLink.Create(UserChatId, "tdc-project-1", SiteAnalysisProjectLinkSource.BootstrapMatched, "", null, null);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldAllowNullCoordinates_ForAnInboundDeepLink()
    {
        var link = SiteAnalysisProjectLink.Create(UserChatId, "tdc-project-1", SiteAnalysisProjectLinkSource.InboundDeepLink, "Al Safa Park", null, null);

        link.ResolvedLatitude.Should().BeNull();
        link.ResolvedLongitude.Should().BeNull();
    }
}
