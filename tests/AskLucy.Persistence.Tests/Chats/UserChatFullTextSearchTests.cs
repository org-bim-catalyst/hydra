using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Domain.Chats;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// Proves message-content and title full-text search (FR-019, research.md Topic 5) against a
/// real SQL Server instance — SQL Server's FTS index population is asynchronous, so this also
/// demonstrates the near-real-time (not synchronous) freshness accepted for FR-019/SC-001a.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class UserChatFullTextSearchTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task SearchAsync_ShouldMatchByTitle()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var matching = UserChat.Create("Quarterly budget planning", userId, null, userId);
        var nonMatching = UserChat.Create("Weekend trip ideas", userId, null, userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.UserChats.AddRange(matching, nonMatching);
            await dbContext.SaveChangesAsync();
        }

        await WaitForFullTextPopulationAsync();

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserChatRepository(readContext);

        var (items, _) = await repository.SearchAsync(
            userId, ConversationView.Active, null, null, "budget", ConversationSort.Newest, null, 50, CancellationToken.None);

        items.Should().ContainSingle(c => c.Id == matching.Id);
    }

    [Fact]
    public async Task SearchAsync_ShouldMatchByMessageContent()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Untitled", userId, null, userId);
        var message = Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "What is our expense reimbursement policy?", null, userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.UserChats.Add(chat);
            dbContext.Messages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        await WaitForFullTextPopulationAsync();

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserChatRepository(readContext);

        var (items, _) = await repository.SearchAsync(
            userId, ConversationView.Active, null, null, "reimbursement", ConversationSort.Newest, null, 50, CancellationToken.None);

        items.Should().ContainSingle(c => c.Id == chat.Id);
    }

    // SQL Server FTS index population is asynchronous (CHANGE_TRACKING AUTO, research.md
    // Topic 5) — a short wait is the simplest way to assert "near-real-time" without coupling
    // this test to sys.dm_fts_index_population internals.
    private static Task WaitForFullTextPopulationAsync() => Task.Delay(TimeSpan.FromSeconds(3));
}
