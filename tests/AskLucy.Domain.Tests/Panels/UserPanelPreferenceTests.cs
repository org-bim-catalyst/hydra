using AskLucy.Domain.Common;
using AskLucy.Domain.Panels;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Panels;

public sealed class UserPanelPreferenceTests
{
    [Fact]
    public void Create_ShouldDefaultToTheDocumentedDefaultOpacity()
    {
        var preference = UserPanelPreference.Create("user-1", "user-1");

        preference.UserId.Should().Be("user-1");
        preference.OpacityPercent.Should().Be(UserPanelPreference.DefaultOpacityPercent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenUserIdIsBlank(string blankUserId)
    {
        var act = () => UserPanelPreference.Create(blankUserId, "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void SetOpacityPercent_ShouldUpdateValueAndAudit()
    {
        var preference = UserPanelPreference.Create("user-1", "user-1");

        preference.SetOpacityPercent(60, "user-1");

        preference.OpacityPercent.Should().Be(60);
        preference.ModifiedAtUtc.Should().NotBeNull();
        preference.ModifiedBy.Should().Be("user-1");
    }

    [Theory]
    [InlineData(10, 40)]
    [InlineData(39, 40)]
    [InlineData(150, 100)]
    [InlineData(101, 100)]
    public void SetOpacityPercent_ShouldClampOutOfRangeValues_AsDefenseInDepth(int input, int expected)
    {
        var preference = UserPanelPreference.Create("user-1", "user-1");

        preference.SetOpacityPercent(input, "user-1");

        preference.OpacityPercent.Should().Be(expected);
    }
}
