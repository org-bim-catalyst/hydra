using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications.Templates;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// T187 (SC-006) — every emitted catalogue type ships an English and an Arabic default for each channel
/// the English set uses, and every shipped seed (both languages) is accepted by the domain: it parses,
/// fits the length limits, and references only variables its type declares. The seeder rejects a bad
/// seed at startup, so this is the build-time guard against one slipping in.
/// </summary>
public sealed class NotificationCatalogCoverageTests
{
    /// <summary>
    /// <c>knowledge-base.updated</c> stays dormant until its emitter exists, and <c>template.test</c> renders the
    /// administrator's own version under test, so neither has a shipped default (see the seeder tests).
    /// </summary>
    private static readonly string[] DeliberatelyUnseeded = [NotificationTypeKeys.KnowledgeBaseUpdated, NotificationTypeKeys.TemplateTest];

    private static readonly IReadOnlyList<TemplateSeed> Seeds = NotificationTemplateSeeder.LoadSeeds();

    private static HashSet<(string Type, NotificationChannel Channel)> KeysOf(string language) =>
        Seeds.Where(s => s.Key.Language == language).Select(s => (s.Key.Type, s.Key.Channel)).ToHashSet();

    [Fact]
    public void EveryEmittedType_ShipsAnEnglishSeed_OnAtLeastOneChannel()
    {
        var english = KeysOf("en").Select(k => k.Type).ToHashSet(StringComparer.Ordinal);

        var missing = NotificationTypeCatalog.All
            .Where(d => d.IsEmitted)
            .Select(d => d.Key)
            .Except(DeliberatelyUnseeded)
            .Where(type => !english.Contains(type));

        missing.Should().BeEmpty("every emitted type needs a default template to render");
    }

    [Fact]
    public void EveryEnglishSeed_HasAnArabicSeed_ForTheSameTypeAndChannel()
    {
        var arabic = KeysOf("ar");

        var missing = KeysOf("en").Where(k => !arabic.Contains(k)).Select(k => $"{k.Type}.{k.Channel}");

        missing.Should().BeEmpty("Arabic needs a version of every English default (T199)");
    }

    [Fact]
    public void EveryArabicSeed_HasAnEnglishCounterpart()
    {
        var english = KeysOf("en");

        var orphans = KeysOf("ar").Where(k => !english.Contains(k)).Select(k => $"{k.Type}.{k.Channel}");

        orphans.Should().BeEmpty("a language without an English twin would have no fallback");
    }

    [Fact]
    public void EverySeed_ShipsOnlyLanguagesTheHubSupports()
    {
        Seeds.Select(s => s.Key.Language).Distinct().Should().BeEquivalentTo(["en", "ar"]);
    }

    [Fact]
    public void EverySeed_ValidatesAgainstItsTypesDeclaredVariables()
    {
        Seeds.Should().NotBeEmpty();
        foreach (var seed in Seeds)
        {
            var template = NotificationTemplate.Create(seed.Key.Type, seed.Key.Channel, seed.Key.Language, seed.Name, DateTime.UtcNow);

            var addDraft = () => template.AddDraft(seed.Content, DateTime.UtcNow);

            addDraft.Should().NotThrow($"seed '{seed.ResourceName}' must parse and validate against '{seed.Key.Type}'");
        }
    }

    [Fact]
    public void EverySeed_ValidatesAgainstItsTypesDeclaredVariables_AndCanBePublished()
    {
        foreach (var seed in Seeds)
        {
            var now = DateTime.UtcNow;
            var template = NotificationTemplate.Create(seed.Key.Type, seed.Key.Channel, seed.Key.Language, seed.Name, now);
            var version = template.AddDraft(seed.Content, now);

            var publish = () => template.Publish(version.Id, "test-user", now);

            publish.Should().NotThrow($"seed '{seed.ResourceName}' is published by the seeder on first start");
        }
    }
}
