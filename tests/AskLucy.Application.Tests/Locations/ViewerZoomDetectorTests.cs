using AskLucy.Application.Locations;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Locations;

/// <summary>
/// specs/038-viewer-poi-zoom US2: keyword-based zoom intent detection — no AI call, fully
/// deterministic. Covers both directions, case-insensitivity, all registered keywords, and
/// null return when no keyword matches.
/// </summary>
public sealed class ViewerZoomDetectorTests
{
    private readonly ViewerZoomDetector _sut = new();

    // --- zoom-in keywords ---

    [Theory]
    [InlineData("zoom in")]
    [InlineData("get closer")]
    [InlineData("fly closer")]
    [InlineData("focus on")]
    [InlineData("come in")]
    [InlineData("move in")]
    [InlineData("zoomed in")]
    [InlineData("closer")]
    public void Detect_ShouldReturnIn_WhenMessageContainsInKeyword(string keyword)
    {
        var result = _sut.Detect(keyword);

        result.Should().NotBeNull();
        result!.Direction.Should().Be("in");
    }

    // --- zoom-out keywords ---

    [Theory]
    [InlineData("zoom out")]
    [InlineData("pull back")]
    [InlineData("fly back")]
    [InlineData("more context")]
    [InlineData("back up")]
    [InlineData("move out")]
    [InlineData("zoom back")]
    [InlineData("wider")]
    public void Detect_ShouldReturnOut_WhenMessageContainsOutKeyword(string keyword)
    {
        var result = _sut.Detect(keyword);

        result.Should().NotBeNull();
        result!.Direction.Should().Be("out");
    }

    // --- case-insensitivity ---

    [Theory]
    [InlineData("ZOOM IN")]
    [InlineData("Zoom In")]
    [InlineData("ZOOM OUT")]
    [InlineData("Pull Back")]
    public void Detect_ShouldBeCaseInsensitive(string message)
    {
        var result = _sut.Detect(message);

        result.Should().NotBeNull();
    }

    // --- embedded in sentence ---

    [Fact]
    public void Detect_ShouldDetectKeywordEmbeddedInSentence()
    {
        var result = _sut.Detect("Can you please zoom in a bit more?");

        result.Should().NotBeNull();
        result!.Direction.Should().Be("in");
    }

    [Fact]
    public void Detect_ShouldDetectOutKeywordEmbeddedInSentence()
    {
        var result = _sut.Detect("Please pull back so I can see the full area.");

        result.Should().NotBeNull();
        result!.Direction.Should().Be("out");
    }

    // --- null when no match ---

    [Theory]
    [InlineData("show me Dubai Mall")]
    [InlineData("what is the weather like")]
    [InlineData("tell me about this building")]
    [InlineData("")]
    public void Detect_ShouldReturnNull_WhenNoZoomKeywordPresent(string message)
    {
        var result = _sut.Detect(message);

        result.Should().BeNull();
    }
}
