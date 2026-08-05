using AskLucy.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Documents;

/// <summary>US5 — reprocessing a replaced version (<c>ClassificationStageHandler</c>) must never silently clobber a user's classification override (FR-025, FR-026).</summary>
public sealed class DocumentClassificationReclassificationTests
{
    [Fact]
    public void ApplyAutomaticReclassification_ShouldUpdateCategoryAndConfidence_WhenStillAutomatic()
    {
        var oldCategoryId = Guid.CreateVersion7();
        var newCategoryId = Guid.CreateVersion7();
        var classification = DocumentClassification.CreateAutomatic(Guid.CreateVersion7(), oldCategoryId, 0.7m, "system");

        classification.ApplyAutomaticReclassification(newCategoryId, 0.95m, "system");

        classification.CategoryId.Should().Be(newCategoryId);
        classification.ConfidenceScore.Should().Be(0.95m);
        classification.Source.Should().Be(DocumentClassificationSource.Automatic);
    }

    [Fact]
    public void ApplyAutomaticReclassification_ShouldBeANoOp_WhenTheUserAlreadyOverrodeIt()
    {
        var overriddenCategoryId = Guid.CreateVersion7();
        var freshlyClassifiedCategoryId = Guid.CreateVersion7();
        var classification = DocumentClassification.CreateAutomatic(Guid.CreateVersion7(), Guid.CreateVersion7(), 0.6m, "system");
        classification.ApplyOverride(overriddenCategoryId, "user-1");

        classification.ApplyAutomaticReclassification(freshlyClassifiedCategoryId, 0.99m, "system");

        classification.CategoryId.Should().Be(overriddenCategoryId);
        classification.Source.Should().Be(DocumentClassificationSource.UserOverride);
        classification.ConfidenceScore.Should().BeNull();
    }
}
