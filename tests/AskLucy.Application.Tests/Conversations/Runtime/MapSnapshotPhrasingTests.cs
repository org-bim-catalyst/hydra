using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/083 — the phrasings that could mean "a picture of it" or "put it on the map" are
/// recognised, and the long-standing navigational "show me &lt;place&gt;" is not.
/// </summary>
public sealed class MapSnapshotPhrasingTests
{
    [Theory]
    [InlineData("how does Al Safa Park 2 look", "Al Safa Park 2")]
    [InlineData("How does Al Safa Park 2 look?", "Al Safa Park 2")]
    [InlineData("what does Dubai Mall look like", "Dubai Mall")]
    [InlineData("what does the new development look like", "new development")]
    [InlineData("please show me how Al Safa Park 2 looks", "Al Safa Park 2")]
    [InlineData("can you tell me what Jebel Ali looks like", "Jebel Ali")]
    public void TheAmbiguousPhrasings_AreCaught_AndNameTheirPlace(string message, string expectedPlace) =>
        MapSnapshotPhrasing.TryDetectAmbiguousPlace(message).Should().Be(expectedPlace);

    [Theory]
    [InlineData("show me Al Safa Park 2")]
    [InlineData("Show me Al Safa Park 2")]
    [InlineData("show me Dubai Mall")]
    [InlineData("take me to Al Safa Park 2")]
    [InlineData("where is Al Safa Park 2")]
    [InlineData("centre on Dubai Mall")]
    [InlineData("zoom in")]
    [InlineData("hello")]
    [InlineData("just chatting")]
    public void TheNavigationalPhrasing_IsNeverCaught(string message) =>
        MapSnapshotPhrasing.TryDetectAmbiguousPlace(message).Should().BeNull(
            "\"show me <place>\" means put it on the map, and has meant that for years");

    [Theory]
    [InlineData("give me a screenshot for Al Safa Park 2")]
    [InlineData("please get me a screenshot for the current location")]
    [InlineData("a snapshot of Dubai Mall")]
    [InlineData("show me an aerial view of the site")]
    [InlineData("what does the site look like from above")]
    public void ARequestThatNamesThePicture_IsNeverCaught(string message) =>
        MapSnapshotPhrasing.TryDetectAmbiguousPlace(message).Should().BeNull(
            "the picture is named outright, so the deciding model can route it without a card");

    [Theory]
    [InlineData("how does the weather look today")]
    [InlineData("what is the weather like generally")]
    [InlineData("is it going to rain")]
    public void TheWeather_IsNeverCaught(string message) =>
        MapSnapshotPhrasing.TryDetectAmbiguousPlace(message).Should().BeNull(
            "\"how does it look\" is the weather's phrasing too, and the weather is never a picture");

    [Theory]
    [InlineData("how does this site look")]
    [InlineData("how does the current location look")]
    [InlineData("how does it look")]
    [InlineData("show me how it looks")]
    public void ABackReferenceToThePlaceOnScreen_IsNotOfferedAMapMove(string message) =>
        MapSnapshotPhrasing.TryDetectAmbiguousPlace(message).Should().BeNull(
            "the card's second row moves the map there, and it is already there");

    [Theory]
    [InlineData("how does that document look")]
    [InlineData("how does the file look")]
    [InlineData("how does the code look")]
    public void SomethingThatIsNotAPlace_IsNotOfferedAMapMove(string message) =>
        MapSnapshotPhrasing.TryDetectAmbiguousPlace(message).Should().BeNull(
            "offering a document a map move is nonsense");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToRead_IsNotCaught(string? message) =>
        MapSnapshotPhrasing.TryDetectAmbiguousPlace(message).Should().BeNull();
}
