using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Prompts;

public sealed class PromptFolderTests
{
    private const string OwnerId = "user-1";

    [Fact]
    public void Create_AtRoot_ShouldHaveDepthZero()
    {
        var folder = PromptFolder.Create(OwnerId, "Marketing", parentFolderId: null, parentDepth: 0, maxNestingDepth: 10, OwnerId);

        folder.Depth.Should().Be(0);
        folder.ParentFolderId.Should().BeNull();
        folder.Name.Should().Be("Marketing");
    }

    [Fact]
    public void Create_NestedInsideAParent_ShouldBeParentDepthPlusOne()
    {
        var parentId = Guid.NewGuid();

        var folder = PromptFolder.Create(OwnerId, "Campaigns", parentFolderId: parentId, parentDepth: 2, maxNestingDepth: 10, OwnerId);

        folder.Depth.Should().Be(3);
        folder.ParentFolderId.Should().Be(parentId);
    }

    [Fact]
    public void Create_ShouldThrow_WhenExceedingMaxNestingDepth()
    {
        var act = () => PromptFolder.Create(OwnerId, "TooDeep", Guid.NewGuid(), parentDepth: 10, maxNestingDepth: 10, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenNameIsBlank(string blankName)
    {
        var act = () => PromptFolder.Create(OwnerId, blankName, null, 0, 10, OwnerId);
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void MoveTo_ShouldRecomputeDepthFromTheNewParent()
    {
        var folder = PromptFolder.Create(OwnerId, "Folder", null, 0, 10, OwnerId);
        var newParentId = Guid.NewGuid();

        folder.MoveTo(newParentId, newParentDepth: 4, maxNestingDepth: 10, OwnerId);

        folder.ParentFolderId.Should().Be(newParentId);
        folder.Depth.Should().Be(5);
    }

    [Fact]
    public void MoveTo_ToRoot_ShouldResetDepthToZero()
    {
        var folder = PromptFolder.Create(OwnerId, "Folder", Guid.NewGuid(), parentDepth: 3, maxNestingDepth: 10, OwnerId);

        folder.MoveTo(null, newParentDepth: 0, maxNestingDepth: 10, OwnerId);

        folder.ParentFolderId.Should().BeNull();
        folder.Depth.Should().Be(0);
    }

    [Fact]
    public void MoveTo_ShouldThrow_WhenTheNewDepthExceedsMaxNestingDepth()
    {
        var folder = PromptFolder.Create(OwnerId, "Folder", null, 0, 10, OwnerId);

        var act = () => folder.MoveTo(Guid.NewGuid(), newParentDepth: 10, maxNestingDepth: 10, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Rename_ShouldTrimAndUpdateName()
    {
        var folder = PromptFolder.Create(OwnerId, "Folder", null, 0, 10, OwnerId);

        folder.Rename("  Renamed  ", OwnerId);

        folder.Name.Should().Be("Renamed");
    }

    [Fact]
    public void Rename_ShouldThrow_WhenNameIsBlank()
    {
        var folder = PromptFolder.Create(OwnerId, "Folder", null, 0, 10, OwnerId);

        var act = () => folder.Rename("   ", OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAudit()
    {
        var folder = PromptFolder.Create(OwnerId, "Folder", null, 0, 10, OwnerId);

        folder.SoftDelete(OwnerId);

        folder.IsDeleted.Should().BeTrue();
        folder.DeletedBy.Should().Be(OwnerId);
    }
}
