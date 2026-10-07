using AskLucy.Domain.Common;
using AskLucy.Domain.Localization;
using FluentAssertions;

namespace AskLucy.Domain.Tests.Notifications;

/// <summary>specs/067 FR-044a — the platform localization setting: disabled and English-only by default, English always kept, only platform languages.</summary>
public sealed class LocalizationSettingTests
{
    private static LocalizationSetting Default() => LocalizationSetting.CreateDefault(new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ADefaultSetting_IsDisabled_AndEnglishOnly()
    {
        var setting = Default();

        setting.IsEnabled.Should().BeFalse();
        setting.SupportedLanguages.Should().Equal("en");
        setting.Id.Should().Be(LocalizationSetting.SingletonId);
    }

    [Fact]
    public void Update_NormalizesCase_RemovesDuplicates_AndPutsEnglishFirst()
    {
        var setting = Default();

        setting.Update(true, ["AR", "en", "ar", "EN"]);

        setting.IsEnabled.Should().BeTrue();
        setting.SupportedLanguages.Should().Equal("en", "ar");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("")]
    public void Update_RequiresEnglish(string only)
    {
        var setting = Default();

        var act = () => setting.Update(true, only.Length == 0 ? [] : [only]);

        act.Should().Throw<DomainRuleViolationException>().WithMessage("*English*");
        setting.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Update_RejectsALanguageThePlatformDoesNotProvide()
    {
        var act = () => Default().Update(true, ["en", "fr"]);

        act.Should().Throw<DomainRuleViolationException>().WithMessage("*'fr'*");
    }

    [Fact]
    public void Supports_EnglishAlways_OthersOnlyWhileEnabledAndListed()
    {
        var setting = Default();
        setting.Update(false, ["en", "ar"]);
        setting.Supports("en").Should().BeTrue();
        setting.Supports("ar").Should().BeFalse();

        setting.Update(true, ["en", "ar"]);
        setting.Supports("AR").Should().BeTrue();
        setting.Supports("fr").Should().BeFalse();
        setting.Supports(null).Should().BeFalse();
    }

    [Fact]
    public void PlatformLanguages_KnowTheirDirection_AndNativeNames()
    {
        PlatformLanguages.Find("ar")!.Direction.Should().Be("rtl");
        PlatformLanguages.Find("en")!.Direction.Should().Be("ltr");
        PlatformLanguages.Find("ar")!.NativeName.Should().Be("العربية");
        PlatformLanguages.Normalize(" AR ").Should().Be("ar");
        PlatformLanguages.IsPlatformLanguage("de").Should().BeFalse();
    }
}
