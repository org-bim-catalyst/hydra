using AskLucy.Application.Localization;
using AskLucy.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>T184 (specs/067 US8) — the FR-044 chain, the removal fallback, and the 30 s cache of the platform setting.</summary>
public sealed class EffectiveLanguageResolverTests
{
    private readonly ILocalizationSettingsProvider _settings = Substitute.For<ILocalizationSettingsProvider>();
    private readonly IUserLanguageStore _users = Substitute.For<IUserLanguageStore>();

    private EffectiveLanguageResolver Resolver(bool enabled, params string[] supported)
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new LocalizationSnapshot(enabled, supported));
        return new EffectiveLanguageResolver(_settings, _users);
    }

    private void UserChose(string? language) =>
        _users.GetPreferredLanguageAsync("u1", Arg.Any<CancellationToken>()).Returns(language);

    [Fact]
    public async Task Disabled_IsAlwaysEnglish_WhateverTheRequestOrTheUserWant()
    {
        UserChose("ar");

        (await Resolver(enabled: false, "en", "ar").ResolveAsync("u1", "ar", TestContext.Current.CancellationToken)).Should().Be("en");
    }

    [Fact]
    public async Task ExplicitLanguage_WinsOverTheUsersChoice_WhenSupported()
    {
        UserChose("en");

        (await Resolver(true, "en", "ar").ResolveAsync("u1", "ar", TestContext.Current.CancellationToken)).Should().Be("ar");
    }

    [Fact]
    public async Task UsersChoice_IsUsed_WhenThereIsNoExplicitLanguage()
    {
        UserChose("ar");

        (await Resolver(true, "en", "ar").ResolveAsync("u1", null, TestContext.Current.CancellationToken)).Should().Be("ar");
    }

    [Fact]
    public async Task AnUnsupportedExplicitLanguage_FallsThroughToTheUsersChoice()
    {
        UserChose("ar");

        (await Resolver(true, "en", "ar").ResolveAsync("u1", "fr", TestContext.Current.CancellationToken)).Should().Be("ar");
    }

    [Fact]
    public async Task RemovingArabic_SendsAnArabicUserBackToEnglish_WithoutLosingTheirChoice()
    {
        UserChose("ar");

        (await Resolver(true, "en").ResolveAsync("u1", null, TestContext.Current.CancellationToken)).Should().Be("en");
        await _users.DidNotReceive().SetPreferredLanguageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fr")]
    [InlineData("")]
    public async Task NoUsableCandidate_IsEnglish(string? chosen)
    {
        UserChose(chosen);

        (await Resolver(true, "en", "ar").ResolveAsync("u1", null, TestContext.Current.CancellationToken)).Should().Be("en");
    }

    [Fact]
    public async Task ARecipientWithoutAnAccount_UsesTheExplicitLanguageOrEnglish_AndNeverLooksAUserUp()
    {
        var resolver = Resolver(true, "en", "ar");

        (await resolver.ResolveAsync(null, "ar", TestContext.Current.CancellationToken)).Should().Be("ar");
        (await resolver.ResolveAsync(null, null, TestContext.Current.CancellationToken)).Should().Be("en");
        await _users.DidNotReceive().GetPreferredLanguageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LanguageCodes_AreMatchedWithoutRegardToCase()
    {
        UserChose("AR");

        (await Resolver(true, "en", "ar").ResolveAsync("u1", null, TestContext.Current.CancellationToken)).Should().Be("ar");
    }

    // ---- the cached provider ----

    private static (CachedLocalizationSettingsProvider Provider, ILocalizationSettingRepository Repository) Provider(
        bool enabled, params string[] supported)
    {
        var repository = Substitute.For<ILocalizationSettingRepository>();
        var setting = AskLucy.Domain.Localization.LocalizationSetting.CreateDefault(DateTime.UtcNow);
        setting.Update(enabled, supported);
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(setting);

        var services = new ServiceCollection().AddScoped(_ => repository).BuildServiceProvider();
        return (new CachedLocalizationSettingsProvider(new MemoryCache(new MemoryCacheOptions()), services.GetRequiredService<IServiceScopeFactory>()), repository);
    }

    [Fact]
    public async Task TheProvider_ReadsOnce_ThenServesFromTheCache()
    {
        var (provider, repository) = Provider(true, "en", "ar");

        var first = await provider.GetAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetAsync(TestContext.Current.CancellationToken);

        first.IsEnabled.Should().BeTrue();
        first.SupportedLanguages.Should().Equal("en", "ar");
        second.Should().BeSameAs(first);
        await repository.Received(1).GetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheProvider_ReadsAgain_AfterAnUpdateEvictsTheCache()
    {
        var (provider, repository) = Provider(true, "en", "ar");
        await provider.GetAsync(TestContext.Current.CancellationToken);

        provider.Evict();
        await provider.GetAsync(TestContext.Current.CancellationToken);

        await repository.Received(2).GetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheCache_LivesThirtySeconds() => CachedLocalizationSettingsProvider.CacheDuration.Should().Be(TimeSpan.FromSeconds(30));

    [Fact]
    public async Task BeforeTheSeedRowExists_ThePlatformIsEnglishOnly()
    {
        var repository = Substitute.For<ILocalizationSettingRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns((AskLucy.Domain.Localization.LocalizationSetting?)null);
        var services = new ServiceCollection().AddScoped(_ => repository).BuildServiceProvider();
        var provider = new CachedLocalizationSettingsProvider(new MemoryCache(new MemoryCacheOptions()), services.GetRequiredService<IServiceScopeFactory>());

        var snapshot = await provider.GetAsync(TestContext.Current.CancellationToken);

        snapshot.IsEnabled.Should().BeFalse();
        snapshot.Supports("ar").Should().BeFalse();
    }
}
