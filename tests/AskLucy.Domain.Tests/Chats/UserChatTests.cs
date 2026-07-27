using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Chats;

public sealed class UserChatTests
{
    [Fact]
    public void Create_ShouldSetOwnerAndAuditFields()
    {
        var chat = UserChat.Create("My chat", "user-1", "session-1", "user-1");

        chat.Title.Should().Be("My chat");
        chat.UserId.Should().Be("user-1");
        chat.SessionId.Should().Be("session-1");
        chat.CreatedBy.Should().Be("user-1");
        chat.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenTitleIsBlank(string blankTitle)
    {
        var act = () => UserChat.Create(blankTitle, "user-1", null, "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Rename_ShouldUpdateTitleAndModifiedAudit()
    {
        var chat = UserChat.Create("Old", "user-1", null, "user-1");
        chat.Rename("New", "user-1");

        chat.Title.Should().Be("New");
        chat.ModifiedBy.Should().Be("user-1");
        chat.ModifiedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAudit_WithoutRemovingTheEntity()
    {
        var chat = UserChat.Create("Chat", "user-1", null, "user-1");
        chat.SoftDelete("user-1");

        chat.IsDeleted.Should().BeTrue();
        chat.DeletedBy.Should().Be("user-1");
        chat.Title.Should().Be("Chat", "soft delete must not erase the underlying data");
    }

    [Fact]
    public void IsOwnedBy_ShouldReflectTheCreatingUser()
    {
        var chat = UserChat.Create("Chat", "owner-1", null, "owner-1");

        chat.IsOwnedBy("owner-1").Should().BeTrue();
        chat.IsOwnedBy("someone-else").Should().BeFalse();
    }
}
