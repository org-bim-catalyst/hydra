using AskLucy.Infrastructure.Ai.Supertonic;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai.Supertonic;

/// <summary>specs/070 — Supertonic's text front end, ported from the upstream reference helper.</summary>
public sealed class SupertonicTextTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("AR", "ar")]
    [InlineData("pt_BR", "pt")]
    [InlineData(" fr ", "fr")]
    [InlineData("zh", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ResolveLanguage_ShouldReduceToASupportedBareCode(string? language, string? expected) =>
        SupertonicText.ResolveLanguage(language).Should().Be(expected);

    [Fact]
    public void Preprocess_ShouldWrapInLanguageTags_AndAppendAFullStop()
    {
        SupertonicText.Preprocess("Hello world", "en").Should().Be("<en>Hello world.</en>");
    }

    [Fact]
    public void Preprocess_ShouldKeepExistingTerminalPunctuation()
    {
        SupertonicText.Preprocess("Are you there?", "en").Should().Be("<en>Are you there?</en>");
    }

    [Fact]
    public void Preprocess_ShouldNormaliseSymbolsQuotesAndSpacing()
    {
        var result = SupertonicText.Preprocess("It’s  a “test” — e.g., one_two @home ,ok 😀", "en");

        result.Should().Be("<en>It's a \"test\" - for example, one two at home,ok.</en>");
    }

    [Fact]
    public void Preprocess_ShouldLeaveArabicTextIntact()
    {
        SupertonicText.Preprocess("مرحبا بك", "ar").Should().Be("<ar>مرحبا بك.</ar>");
    }

    [Fact]
    public void Chunk_ShouldPackSentencesUpToTheLimit_AndSplitParagraphs()
    {
        var sentence = new string('a', 140) + ".";
        var text = $"{sentence} {sentence} {sentence}\n\nSecond paragraph.";

        var chunks = SupertonicText.Chunk(text, "en");

        chunks.Should().Equal($"{sentence} {sentence}", sentence, "Second paragraph.");
    }

    [Fact]
    public void Chunk_ShouldNotSplitAfterAnAbbreviation()
    {
        SupertonicText.Chunk("Dr. Smith arrived. He sat down.", "en")
            .Should().ContainSingle().Which.Should().Be("Dr. Smith arrived. He sat down.");
    }

    [Fact]
    public void Chunk_ShouldUseTheShorterLimitForKoreanAndJapanese()
    {
        var sentence = new string('가', 70) + ".";

        SupertonicText.Chunk($"{sentence} {sentence}", "ko").Should().HaveCount(2);
        SupertonicText.Chunk($"{sentence} {sentence}", "en").Should().ContainSingle();
    }

    [Fact]
    public void Chunk_ShouldReturnNothingForWhitespace()
    {
        SupertonicText.Chunk("   \n\n  ", "en").Should().BeEmpty();
    }

    [Fact]
    public void ToTokenIds_ShouldMapCodeUnitsThroughTheIndexer_AndZeroAnythingOutsideIt()
    {
        var indexer = new long[128];
        indexer['<'] = 1;
        indexer['a'] = 7;

        SupertonicText.ToTokenIds("<aم", indexer).Should().Equal(1, 7, 0);
    }
}
