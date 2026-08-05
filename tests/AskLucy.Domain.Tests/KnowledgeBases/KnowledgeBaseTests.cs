using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.KnowledgeBases;

public sealed class KnowledgeBaseTests
{
    [Fact]
    public void Create_ShouldSetOwnerAndDraftStatus()
    {
        var knowledgeBase = KnowledgeBase.Create("BIM Standards", "user-1", "user-1");

        knowledgeBase.Name.Should().Be("BIM Standards");
        knowledgeBase.OwnerId.Should().Be("user-1");
        knowledgeBase.Status.Should().Be(KnowledgeBaseStatus.Draft);
        knowledgeBase.Visibility.Should().Be(KnowledgeBaseVisibility.Private);
        knowledgeBase.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenNameIsBlank(string blankName)
    {
        var act = () => KnowledgeBase.Create(blankName, "user-1", "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void UpdateDetails_ShouldReplaceEveryEditableField()
    {
        var knowledgeBase = KnowledgeBase.Create("Old", "user-1", "user-1");

        knowledgeBase.UpdateDetails("New", "desc", "#FFFFFF", "folder", categoryId: Guid.NewGuid(), notes: "note", "user-1");

        knowledgeBase.Name.Should().Be("New");
        knowledgeBase.Description.Should().Be("desc");
        knowledgeBase.Color.Should().Be("#FFFFFF");
        knowledgeBase.Icon.Should().Be("folder");
        knowledgeBase.CategoryId.Should().NotBeNull();
        knowledgeBase.Notes.Should().Be("note");
        knowledgeBase.ModifiedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void UpdateDetails_ShouldThrow_WhenNameIsBlank()
    {
        var knowledgeBase = KnowledgeBase.Create("Old", "user-1", "user-1");

        var act = () => knowledgeBase.UpdateDetails("", null, null, null, null, null, "user-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Activate_ShouldTransitionDraftToActive()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.Activate("user-1");

        knowledgeBase.Status.Should().Be(KnowledgeBaseStatus.Active);
    }

    [Fact]
    public void Activate_ShouldThrow_WhenNotDraft()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.Activate("user-1");

        var act = () => knowledgeBase.Activate("user-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Archive_ShouldThrow_WhenNotActive()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        var act = () => knowledgeBase.Archive("user-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Archive_ThenRestore_ShouldReturnToActive()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.Activate("user-1");
        knowledgeBase.Archive("user-1");

        knowledgeBase.Restore("user-1");

        knowledgeBase.Status.Should().Be(KnowledgeBaseStatus.Active);
    }

    [Fact]
    public void SoftDelete_ShouldScheduleAutomaticPurgeThirtyDaysOut()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.SoftDelete("user-1");

        knowledgeBase.IsDeleted.Should().BeTrue();
        knowledgeBase.PurgeScheduledAtUtc.Should().NotBeNull();
        knowledgeBase.PurgeScheduledAtUtc.Should().BeCloseTo(knowledgeBase.DeletedAtUtc!.Value.AddDays(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Restore_AfterSoftDelete_ShouldCancelThePendingPurgeAndPreserveStatus()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.Activate("user-1");
        knowledgeBase.SoftDelete("user-1");

        knowledgeBase.Restore("user-1");

        knowledgeBase.IsDeleted.Should().BeFalse();
        knowledgeBase.PurgeScheduledAtUtc.Should().BeNull();
        knowledgeBase.Status.Should().Be(KnowledgeBaseStatus.Active, "restore must preserve whatever status existed before delete");
    }

    [Fact]
    public void MarkFavorite_ShouldBeIdempotent()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.MarkFavorite("user-1");
        var afterFirst = knowledgeBase.ModifiedAtUtc;
        knowledgeBase.MarkFavorite("user-1");

        knowledgeBase.IsFavorite.Should().BeTrue();
        knowledgeBase.ModifiedAtUtc.Should().Be(afterFirst, "a second call to an already-favorited entity must be a no-op");
    }

    [Fact]
    public void Pin_Unpin_ShouldTogglePinnedAtUtc()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.Pin("user-1");
        knowledgeBase.PinnedAtUtc.Should().NotBeNull();

        knowledgeBase.Unpin("user-1");
        knowledgeBase.PinnedAtUtc.Should().BeNull();
    }

    [Fact]
    public void AddTag_ShouldBeCaseInsensitivelyDeduplicated()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.AddTag("Revit", "user-1", "user-1");
        knowledgeBase.AddTag("revit", "user-1", "user-1");

        knowledgeBase.Tags.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyDocumentAdded_ShouldIncrementCachedStatistics()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.ApplyDocumentAdded(pageCount: 5, sizeBytes: 1000, "user-1");

        knowledgeBase.DocumentCount.Should().Be(1);
        knowledgeBase.TotalPageCount.Should().Be(5);
        knowledgeBase.StorageSizeBytes.Should().Be(1000);
    }

    [Fact]
    public void ApplyDocumentRemoved_ShouldNeverGoNegative()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.ApplyDocumentRemoved(pageCount: 5, sizeBytes: 1000, "user-1");

        knowledgeBase.DocumentCount.Should().Be(0);
        knowledgeBase.TotalPageCount.Should().Be(0);
        knowledgeBase.StorageSizeBytes.Should().Be(0);
    }

    [Fact]
    public void IsOwnedBy_ShouldReflectTheCreatingUser()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "owner-1", "owner-1");

        knowledgeBase.IsOwnedBy("owner-1").Should().BeTrue();
        knowledgeBase.IsOwnedBy("someone-else").Should().BeFalse();
    }

    [Fact]
    public void Create_ShouldDefaultVectorStoreProviderToPinecone()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.VectorStoreProvider.Should().Be(VectorStoreProvider.Pinecone);
    }

    [Fact]
    public void UpdateRetrievalSettings_ShouldSetVectorStoreProvider_WhenNotRequiringDataResidency()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.UpdateRetrievalSettings(
            ChunkingStrategy.Recursive, embeddingProviderId: null, EmbeddingHostingType.Cloud,
            VectorStoreProvider.Pinecone, requiresDataResidency: false, "user-1");

        knowledgeBase.VectorStoreProvider.Should().Be(VectorStoreProvider.Pinecone);
    }

    [Fact]
    public void UpdateRetrievalSettings_ShouldThrow_WhenRequiresDataResidencyAndVectorStoreProviderIsPinecone()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        var act = () => knowledgeBase.UpdateRetrievalSettings(
            ChunkingStrategy.Recursive, embeddingProviderId: null, EmbeddingHostingType.Local,
            VectorStoreProvider.Pinecone, requiresDataResidency: true, "user-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void UpdateRetrievalSettings_ShouldSucceed_WhenRequiresDataResidencyAndVectorStoreProviderIsSqlServer()
    {
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");

        knowledgeBase.UpdateRetrievalSettings(
            ChunkingStrategy.Recursive, embeddingProviderId: null, EmbeddingHostingType.Local,
            VectorStoreProvider.SqlServer, requiresDataResidency: true, "user-1");

        knowledgeBase.VectorStoreProvider.Should().Be(VectorStoreProvider.SqlServer);
        knowledgeBase.RequiresDataResidency.Should().BeTrue();
    }
}
