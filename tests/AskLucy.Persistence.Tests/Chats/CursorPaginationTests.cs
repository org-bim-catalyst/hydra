using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Domain.Chats;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// Proves cursor (keyset) pagination for both the conversation list and a single
/// conversation's message list returns stable, non-duplicated, non-skipped pages — including
/// when a new row is inserted between page loads, which offset/skip-take pagination would
/// duplicate or skip (constitution &#167;6, research.md Topic 6).
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class CursorPaginationTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task SearchAsync_ShouldPageThroughAllConversations_WithoutDuplicatesOrGaps()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chats = Enumerable.Range(0, 5).Select(i => UserChat.Create($"Chat {i}", userId, null, userId)).ToList();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.UserChats.AddRange(chats);
            await dbContext.SaveChangesAsync();
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var dbContext = fixture.CreateDbContext();
            var repository = new UserChatRepository(dbContext);
            var (items, nextCursor) = await repository.SearchAsync(
                userId, ConversationView.Active, null, null, null, ConversationSort.Oldest, cursor, pageSize: 2, CancellationToken.None);

            seen.AddRange(items.Select(c => c.Id));
            cursor = nextCursor;
        } while (cursor is not null);

        seen.Should().BeEquivalentTo(chats.Select(c => c.Id), options => options.WithoutStrictOrdering());
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SearchAsync_ShouldNotDuplicateOrSkip_WhenANewConversationIsInsertedBetweenPages()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chats = Enumerable.Range(0, 3).Select(i => UserChat.Create($"Chat {i}", userId, null, userId)).ToList();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.UserChats.AddRange(chats);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserChatRepository(readContext);

        var (firstPage, cursor) = await repository.SearchAsync(
            userId, ConversationView.Active, null, null, null, ConversationSort.Oldest, null, pageSize: 2, CancellationToken.None);

        // Insert a new conversation "after" the cursor position in creation order.
        var insertedLate = UserChat.Create("Inserted late", userId, null, userId);
        await using (var writeContext = fixture.CreateDbContext())
        {
            writeContext.UserChats.Add(insertedLate);
            await writeContext.SaveChangesAsync();
        }

        var (secondPage, _) = await repository.SearchAsync(
            userId, ConversationView.Active, null, null, null, ConversationSort.Oldest, cursor, pageSize: 10, CancellationToken.None);

        firstPage.Select(c => c.Id).Intersect(secondPage.Select(c => c.Id)).Should().BeEmpty("no row should appear twice across pages");
        secondPage.Should().Contain(c => c.Id == insertedLate.Id);
        secondPage.Should().Contain(c => c.Id == chats[2].Id, "the third original conversation must not be skipped");
    }

    [Fact]
    public async Task ListPagedByChatIdAsync_ShouldPageThroughAllMessages_InOrder()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Chat", userId, null, userId);
        var messages = Enumerable.Range(0, 5)
            .Select(i => Message.Create(chat.Id, MessageRole.User, MessageKind.Text, $"Message {i}", null, userId))
            .ToList();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.UserChats.Add(chat);
            dbContext.Messages.AddRange(messages);
            await dbContext.SaveChangesAsync();
        }

        var seenContent = new List<string>();
        string? cursor = null;
        do
        {
            await using var dbContext = fixture.CreateDbContext();
            var repository = new AskLucy.Persistence.Repositories.MessageRepository(dbContext);
            var (items, nextCursor) = await repository.ListPagedByChatIdAsync(chat.Id, cursor, pageSize: 2, CancellationToken.None);

            seenContent.AddRange(items.Select(m => m.Content));
            cursor = nextCursor;
        } while (cursor is not null);

        seenContent.Should().Equal(messages.Select(m => m.Content), "messages must page through in their original chronological order");
    }
}
