using AskLucy.Application.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>specs/077 — which building names carry a site's name.</summary>
public sealed class SiteNameMatcherTests
{
    [Theory]
    [InlineData("BurJuman Mall", "burjuman")]
    [InlineData("Bur Juman Shopping Center", "burjuman")]
    [InlineData("The Dubai Mall", "dubaimall")]
    [InlineData("Mall of Oman", "mallofoman")]
    public void CoresOf_StripsGenericWordsButKeepsPlaceNamesWhole(string name, string expected) =>
        SiteNameMatcher.CoresOf([name]).Should().ContainSingle().Which.Should().Be(expected);

    [Fact]
    public void CoresOf_DropsCoresTooShortToIdentifyASite() =>
        SiteNameMatcher.CoresOf(["The Mall", "ABC", null, " "]).Should().BeEmpty();

    [Theory]
    [InlineData("BurJuman Business Tower")]
    [InlineData("Bur Juman Arjaan by Rotana")]
    [InlineData("Burjman Office Tower")]
    public void Matches_TheSameDevelopmentSpacedOrMistyped(string name) =>
        SiteNameMatcher.Matches([name], SiteNameMatcher.CoresOf(["BurJuman Mall"])).Should().BeTrue();

    [Theory]
    [InlineData("City Seasons Towers Hotel")]
    [InlineData("Dubai Creek Tower")]
    public void Matches_NothingForAnUnrelatedBuilding(string name) =>
        SiteNameMatcher.Matches([name], SiteNameMatcher.CoresOf(["BurJuman Mall"])).Should().BeFalse();

    [Fact]
    public void Matches_NeverTiesEveryDubaiBuildingToTheDubaiMall() =>
        SiteNameMatcher.Matches(["Dubai Marina Tower"], SiteNameMatcher.CoresOf(["The Dubai Mall"])).Should().BeFalse();
}
