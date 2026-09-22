using AskLucy.Application.Ai;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

public sealed class CredentialHintFormatterTests
{
    [Fact]
    public void Format_ShouldReturnFirstAndLastFourCharacters_ForATypicalKey()
    {
        var hint = CredentialHintFormatter.Format("sk-ant-api03-C5fxxxxxxxxygAA");

        hint.Should().Be("sk-a...ygAA");
    }

    [Fact]
    public void Format_ShouldSplitNonOverlappingHalves_AtTheEightCharacterBoundary()
    {
        var hint = CredentialHintFormatter.Format("12345678");

        hint.Should().Be("1234...5678");
    }

    [Fact]
    public void Format_ShouldReturnFullyMaskedPlaceholder_ForAKeyShorterThanEightCharacters()
    {
        var hint = CredentialHintFormatter.Format("abc123");

        hint.Should().Be("****");
    }
}
