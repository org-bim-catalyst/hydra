using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>English typed on the Arabic keyboard layout is recognised, and nothing else is caught.</summary>
public sealed class KeyboardLayoutMisreadTests
{
    [Fact]
    public void TheMessageThatOpenedTheEditorByMistake_IsDecodedToEnglish()
    {
        // Typed with the Arabic layout on: the user's real message from the field.
        var decoded = KeyboardLayoutMisread.TryDecode("احص ةث لاعقتهشى 'شمم");

        decoded.Should().NotBeNull();
        // The user also mistyped a key ("hpw" for "how"), so the decode is approximate - which is why Lucy confirms it.
        decoded.Should().Be("hpw me burjian 'all");
    }

    [Theory]
    [InlineData("اخص ةث", "how me")]
    [InlineData("ا خص", "h ow")]
    public void Decode_MapsEachKeyToItsLatinLetter(string typed, string expected) =>
        KeyboardLayoutMisread.Decode(typed).Should().Be(expected);

    [Fact]
    public void LamAlef_IsTheBKey_EitherAsOneGlyphOrTwoLetters()
    {
        KeyboardLayoutMisread.Decode("لا").Should().Be("b");
        KeyboardLayoutMisread.Decode("ﻻ").Should().Be("b");
        KeyboardLayoutMisread.Decode("لاعق").Should().Be("bur");
    }

    [Fact]
    public void KeepsSpacesDigitsAndPunctuation() =>
        KeyboardLayoutMisread.Decode("اخص 12 ؟").Should().Be("how 12 ؟");

    [Theory]
    [InlineData("Show me Burjuman Mall")]
    [InlineData("How big is the site?")]
    [InlineData("edit the outline")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    public void OrdinaryEnglish_IsNeverCaught(string message) =>
        KeyboardLayoutMisread.TryDecode(message).Should().BeNull();

    [Theory]
    [InlineData("مرحبا كيف حالك اليوم")]
    [InlineData("أريد أن أرى مركز دبي التجاري")]
    [InlineData("ما هي مساحة الموقع؟")]
    [InlineData("عدّل حدود الموقع من فضلك")]
    [InlineData("شكرا جزيلا")]
    public void GenuineArabic_IsNeverCaught_SoItsCapabilitiesKeepWorking(string message) =>
        KeyboardLayoutMisread.TryDecode(message).Should().BeNull();

    [Fact]
    public void ArabicThatDecodesToLetterSoup_IsNotCaught() =>
        KeyboardLayoutMisread.TryDecode("ضصثقفغعهخحجد").Should().BeNull();

    [Fact]
    public void AMessageMixingLanguages_IsNotCaught() =>
        KeyboardLayoutMisread.TryDecode("Show me مركز دبي").Should().BeNull();

    [Fact]
    public void OneMatchingWordIsNotEnough() =>
        KeyboardLayoutMisread.TryDecode("اخص ضصثق").Should().BeNull();

    [Fact]
    public void TheInstruction_QuotesTheDecodedText_AndForbidsActing()
    {
        var instruction = KeyboardLayoutMisread.ClarificationInstruction("how me burjian all");

        instruction.Should().Contain("how me burjian all");
        instruction.Should().Contain("Do NOT act on it");
        instruction.Should().Contain("confirm");
    }
}
