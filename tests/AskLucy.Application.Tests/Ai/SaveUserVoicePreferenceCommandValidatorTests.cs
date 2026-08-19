using AskLucy.Application.Ai.Commands.SaveUserVoicePreference;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/026-floating-chat-assistant FR-017, data-model.md validation rule —
/// `DefaultLanguage`, when present, must be one of the product's supported codes;
/// rejected with a specific message, never silently coerced (constitution §2.VIII).</summary>
public sealed class SaveUserVoicePreferenceCommandValidatorTests
{
    private readonly SaveUserVoicePreferenceCommandValidator _validator = new();

    private static SaveUserVoicePreferenceCommand MakeCommand(string? defaultLanguage) =>
        new("PushToTalk", false, null, null, null, null, null, defaultLanguage);

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData(null)]
    public void Validate_ShouldPass_ForSupportedOrNullLanguage(string? language)
    {
        var result = _validator.Validate(MakeCommand(language));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("EN")]
    [InlineData("english")]
    [InlineData("")]
    public void Validate_ShouldFail_ForAnUnsupportedLanguageCode(string language)
    {
        var result = _validator.Validate(MakeCommand(language));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(SaveUserVoicePreferenceCommand.DefaultLanguage));
    }
}
