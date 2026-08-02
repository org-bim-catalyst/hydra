using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Domain.Chats;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// Proves the full Delete → Recently Deleted → Purge lifecycle against a real SQL Server
/// instance (User Story 4). Most importantly, proves <see cref="UserChatRepository.PurgeAsync"/>
/// actually removes the row — <see cref="AskLucy.Persistence.Interceptors.AuditSaveChangesInterceptor"/>
/// unconditionally converts a tracked hard delete into a soft delete, so this guards against a
/// regression where Purge is reimplemented via load+Remove+SaveChanges and silently stops
/// deleting anything.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class ConversationLifecyclePersistenceTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task SoftDelete_ShouldHideFromActiveView_AndAppearUnderDeletedView()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Chat", userId, null, userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var toDelete = await dbContext.UserChats.SingleAsync(c => c.Id == chat.Id);
            toDelete.SoftDelete(userId);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserChatRepository(readContext);

        var (activeItems, _) = await repository.SearchAsync(
            userId, ConversationView.Active, null, null, null, ConversationSort.Newest, null, 50, CancellationToken.None);
        var (deletedItems, _) = await repository.SearchAsync(
            userId, ConversationView.Deleted, null, null, null, ConversationSort.Newest, null, 50, CancellationToken.None);

        activeItems.Should().NotContain(c => c.Id == chat.Id);
        deletedItems.Should().ContainSingle(c => c.Id == chat.Id);
    }

    [Fact]
    public async Task PurgeAsync_ShouldActuallyRemoveTheRow_NotJustSoftDeleteItAgain()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Chat to purge", userId, null, userId);
        var message = Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "Hello", null, userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            dbContext.Messages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        await using (var purgeContext = fixture.CreateDbContext())
        {
            var repository = new UserChatRepository(purgeContext);
            await repository.PurgeAsync(chat.Id, CancellationToken.None);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var chatStillExists = await verifyContext.UserChats.IgnoreQueryFilters().AnyAsync(c => c.Id == chat.Id);
        var messageStillExists = await verifyContext.Messages.IgnoreQueryFilters().AnyAsync(m => m.Id == message.Id);

        chatStillExists.Should().BeFalse("Purge must physically remove the row, not merely soft-delete it again");
        messageStillExists.Should().BeFalse("messages must cascade-delete when their conversation is purged");
    }
}
