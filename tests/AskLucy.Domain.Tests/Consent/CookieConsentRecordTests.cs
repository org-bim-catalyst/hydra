using AskLucy.Domain.Common;
using AskLucy.Domain.Consent;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Consent;

public sealed class CookieConsentRecordTests
{
    [Fact]
    public void Create_ShouldSetFieldsAndAuditData()
    {
        var record = CookieConsentRecord.Create("user-1", "2026-07-30.1", functionalAccepted: true, analyticsAccepted: false, marketingAccepted: false);

        record.UserId.Should().Be("user-1");
        record.PolicyVersion.Should().Be("2026-07-30.1");
        record.FunctionalAccepted.Should().BeTrue();
        record.AnalyticsAccepted.Should().BeFalse();
        record.MarketingAccepted.Should().BeFalse();
        record.CreatedBy.Should().Be("user-1");
        record.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenUserIdIsBlank(string blankUserId)
    {
        var act = () => CookieConsentRecord.Create(blankUserId, "2026-07-30.1", true, true, true);
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenPolicyVersionIsBlank(string blankVersion)
    {
        var act = () => CookieConsentRecord.Create("user-1", blankVersion, true, true, true);
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Record_ShouldExposeNoMutatorMethods()
    {
        // Append-only by design (research.md Topic 2): a preference change must be a new
        // inserted row, never a mutation of an existing one — enforced here by asserting
        // the public surface has no method beyond the static factory and inherited members.
        var publicInstanceMethods = typeof(CookieConsentRecord)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        publicInstanceMethods.Should().BeEmpty("CookieConsentRecord must be immutable after creation");
    }
}
