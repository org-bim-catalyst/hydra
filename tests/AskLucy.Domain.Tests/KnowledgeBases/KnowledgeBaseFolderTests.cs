using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.KnowledgeBases;

public sealed class KnowledgeBaseFolderTests
{
    private static readonly Guid KnowledgeBaseId = Guid.NewGuid();

    [Fact]
    public void Create_AtRoot_ShouldHaveDepthZero()
    {
        var folder = KnowledgeBaseFolder.Create(KnowledgeBaseId, "Contracts", parentFolderId: null, parentDepth: 0, maxNestingDepth: 10, "user-1");

        folder.Depth.Should().Be(0);
        folder.ParentFolderId.Should().BeNull();
        folder.Name.Should().Be("Contracts");
    }

    [Fact]
    public void Create_NestedInsideAParent_ShouldBeParentDepthPlusOne()
    {
        var parentId = Guid.NewGuid();

        var folder = KnowledgeBaseFolder.Create(KnowledgeBaseId, "Client A", parentFolderId: parentId, parentDepth: 2, maxNestingDepth: 10, "user-1");

        folder.Depth.Should().Be(3);
        folder.ParentFolderId.Should().Be(parentId);
    }

    [Fact]
    public void Create_ShouldThrow_WhenExceedingMaxNestingDepth()
    {
        var act = () => KnowledgeBaseFolder.Create(KnowledgeBaseId, "TooDeep", Guid.NewGuid(), parentDepth: 10, maxNestingDepth: 10, "user-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenNameIsBlank(string blankName)
    {
        var act = () => KnowledgeBaseFolder.Create(KnowledgeBaseId, blankName, null, 0, 10, "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void MoveTo_ShouldRecomputeDepthFromTheNewParent()
    {
        var folder = KnowledgeBaseFolder.Create(KnowledgeBaseId, "Folder", null, 0, 10, "user-1");
        var newParentId = Guid.NewGuid();

        folder.MoveTo(newParentId, newParentDepth: 4, maxNestingDepth: 10, "user-1");

        folder.ParentFolderId.Should().Be(newParentId);
        folder.Depth.Should().Be(5);
    }

    [Fact]
    public void MoveTo_ShouldThrow_WhenTheNewDepthExceedsMaxNestingDepth()
    {
        var folder = KnowledgeBaseFolder.Create(KnowledgeBaseId, "Folder", null, 0, 10, "user-1");

        var act = () => folder.MoveTo(Guid.NewGuid(), newParentDepth: 10, maxNestingDepth: 10, "user-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAudit()
    {
        var folder = KnowledgeBaseFolder.Create(KnowledgeBaseId, "Folder", null, 0, 10, "user-1");

        folder.SoftDelete("user-1");

        folder.IsDeleted.Should().BeTrue();
        folder.DeletedBy.Should().Be("user-1");
    }
}
