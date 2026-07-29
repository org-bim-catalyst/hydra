using AskLucy.Domain.Chats;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Chats;

/// <summary>Covers FR-013's local title-derivation algorithm (research.md Topic 4) — no AI call, deterministic.</summary>
public sealed class ConversationTitleGeneratorTests
{
    [Fact]
    public void DeriveFrom_ShouldReturnShortMessageUnchanged()
    {
        var title = ConversationTitleGenerator.DeriveFrom("How do I file expenses?");

        title.Should().Be("How do I file expenses?");
    }

    [Fact]
    public void DeriveFrom_ShouldStripMarkdownFormatting()
    {
        var title = ConversationTitleGenerator.DeriveFrom("**Please** help with #tax filing [today](https://example.com)");

        title.Should().NotContain("**").And.NotContain("#").And.NotContain("[").And.NotContain("]");
    }

    [Fact]
    public void DeriveFrom_ShouldCollapseNewlinesAndWhitespace()
    {
        var title = ConversationTitleGenerator.DeriveFrom("Hello\n\n   world  \t there");

        title.Should().Be("Hello world there");
    }

    [Fact]
    public void DeriveFrom_ShouldTruncateAtWordBoundary_WithEllipsis_WhenOver60Characters()
    {
        var longMessage = string.Join(' ', Enumerable.Repeat("word", 20));

        var title = ConversationTitleGenerator.DeriveFrom(longMessage);

        title.Length.Should().BeLessThanOrEqualTo(61); // 60 chars + ellipsis
        title.Should().EndWith("…");
        title.Should().NotContain("  ");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveFrom_ShouldReturnEmpty_ForBlankInput(string blank)
    {
        ConversationTitleGenerator.DeriveFrom(blank).Should().BeEmpty();
    }
}
