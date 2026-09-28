using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications.Templates;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>T011/T050 — every shipped seed parses to a real, InApp-supporting catalogue type, and
/// (SC-006) every emitted InApp type ships a default so it always has something to render (the sole,
/// deliberate exception is <c>knowledge-base.updated</c>, which stays dormant until its emitter
/// exists — see the specs/067 final report).</summary>
public sealed class NotificationTemplateSeederTests
{
    private static readonly string[] DeliberatelyUnseeded = [NotificationTypeKeys.KnowledgeBaseUpdated];

    [Fact]
    public void LoadSeeds_ShouldParseEveryEmbeddedFile_AgainstAKnownCatalogueType()
    {
        var seeds = NotificationTemplateSeeder.LoadSeeds();

        seeds.Should().NotBeEmpty();
        foreach (var seed in seeds)
        {
            NotificationTypeCatalog.TryGet(seed.Key.Type, out var definition).Should().BeTrue($"seed '{seed.ResourceName}' names an unknown type");
            definition!.Supports(seed.Key.Channel).Should().BeTrue($"seed '{seed.ResourceName}' uses a channel '{seed.Key.Type}' doesn't send on");
        }
    }

    [Fact]
    public void LoadSeeds_ShouldCoverEveryEmittedInAppType_ExceptTheKnownDormantOnes()
    {
        var seededTypes = NotificationTemplateSeeder.LoadSeeds()
            .Where(s => s.Key.Channel == NotificationChannel.InApp)
            .Select(s => s.Key.Type)
            .ToHashSet(StringComparer.Ordinal);

        var expected = NotificationTypeCatalog.All
            .Where(d => d.IsEmitted && d.Supports(NotificationChannel.InApp))
            .Select(d => d.Key)
            .Except(DeliberatelyUnseeded);

        seededTypes.Should().Contain(expected);
    }

    [Fact]
    public void LoadSeeds_ShouldHaveNoDuplicateKeys()
    {
        var seeds = NotificationTemplateSeeder.LoadSeeds();

        seeds.Select(s => s.Key).Should().OnlyHaveUniqueItems();
    }
}
