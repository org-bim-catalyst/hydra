using AskLucy.Application.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Common;

public sealed class PolicyConditionMatcherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_ShouldReturnTrue_WhenConditionsAreEmpty(string? conditionsJson)
    {
        PolicyConditionMatcher.Matches(conditionsJson, "{\"amount\":100}").Should().BeTrue();
    }

    [Fact]
    public void Matches_ShouldReturnTrue_WhenEveryConditionEqualsTheActualInput()
    {
        var conditionsJson = "{\"recipient\":\"team@example.com\"}";
        var inputJson = "{\"recipient\":\"team@example.com\",\"subject\":\"Report\"}";

        PolicyConditionMatcher.Matches(conditionsJson, inputJson).Should().BeTrue();
    }

    [Fact]
    public void Matches_ShouldReturnFalse_WhenAConditionValueDiffers()
    {
        var conditionsJson = "{\"recipient\":\"team@example.com\"}";
        var inputJson = "{\"recipient\":\"someone-else@example.com\"}";

        PolicyConditionMatcher.Matches(conditionsJson, inputJson).Should().BeFalse();
    }

    [Fact]
    public void Matches_ShouldReturnFalse_WhenAConditionPropertyIsMissingFromInput()
    {
        var conditionsJson = "{\"recipient\":\"team@example.com\"}";
        var inputJson = "{\"subject\":\"Report\"}";

        PolicyConditionMatcher.Matches(conditionsJson, inputJson).Should().BeFalse();
    }

    [Fact]
    public void Matches_ShouldRequireAllConditions_NotJustOne()
    {
        var conditionsJson = "{\"recipient\":\"team@example.com\",\"amount\":100}";
        var inputJson = "{\"recipient\":\"team@example.com\",\"amount\":200}";

        PolicyConditionMatcher.Matches(conditionsJson, inputJson).Should().BeFalse();
    }
}
