using AskLucy.Application.PointsOfInterest;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.PointsOfInterest;

/// <summary>specs/090 — a category phrase must resolve to exactly one tag definition, or to nothing at all; guessing a different category answers a different question.</summary>
public sealed class PoiCategoryCatalogTests
{
    private readonly PoiCategoryCatalog _catalog = new();

    [Theory]
    [InlineData("school", "school")]
    [InlineData("schools", "school")]
    [InlineData("Schools", "school")]
    [InlineData("مدارس", "school")]
    [InlineData("hospitals", "hospital")]
    [InlineData("mosques", "mosque")]
    [InlineData("مساجد", "mosque")]
    [InlineData("pharmacies", "pharmacy")]
    [InlineData("petrol stations", "fuel")]
    [InlineData("gas stations", "fuel")]
    [InlineData("cafés", "cafe")]
    public void TryResolve_ShouldResolveCommonPhrases(string phrase, string expectedKey)
    {
        _catalog.TryResolve(phrase, out var category).Should().BeTrue();
        category.Key.Should().Be(expectedKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("spaceports")]
    [InlineData("something nobody maps")]
    public void TryResolve_ShouldRejectWhatItCannotMap(string phrase)
    {
        _catalog.TryResolve(phrase, out _).Should().BeFalse();
    }

    [Fact]
    public void EveryCategory_ShouldHaveADistinctKeyColourAndShape()
    {
        var all = _catalog.All;

        all.Select(c => c.Key).Should().OnlyHaveUniqueItems();
        // Colours distinguish layers on the map; two categories sharing one would defeat that.
        all.Select(c => c.Color).Should().OnlyHaveUniqueItems();
        all.Should().OnlyContain(c => c.TagGroups.Count > 0);
        all.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.DisplayName));
    }

    [Fact]
    public void DescribeSupported_ShouldNameEveryCategory()
    {
        var description = _catalog.DescribeSupported();

        foreach (var category in _catalog.All)
        {
            description.Should().Contain(category.Key.Replace('_', ' '));
        }
    }
}
