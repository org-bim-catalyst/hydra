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
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.AddRange(matching, nonMatching);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserChatRepository(readContext);

        var items = await SearchUntilAsync(
            repository, userId, "budget", results => results.Any(c => c.Id == matching.Id));

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
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            dbContext.Messages.Add(message);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserChatRepository(readContext);

        var items = await SearchUntilAsync(
            repository, userId, "reimbursement", results => results.Any(c => c.Id == chat.Id));

        items.Should().ContainSingle(c => c.Id == chat.Id);
    }

    // SQL Server FTS index population is asynchronous (CHANGE_TRACKING AUTO, research.md
    // Topic 5). Rather than a single fixed delay — which proved too short under CI load,
    // where the shared test database's catalog can lag well past "near-real-time" — poll the
    // actual search behavior under test until the expected row appears or the deadline passes,
    // returning whatever the last attempt saw so a genuine mismatch still fails with a clear
    // assertion.
    private static async Task<IReadOnlyList<UserChat>> SearchUntilAsync(
        UserChatRepository repository, string userId, string searchTerm, Func<IReadOnlyList<UserChat>, bool> isReady)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        IReadOnlyList<UserChat> items = [];
        do
        {
            (items, _) = await repository.SearchAsync(
                userId, ConversationView.Active, null, null, searchTerm, ConversationSort.Newest, null, 50, CancellationToken.None);
            if (isReady(items))
            {
                return items;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        } while (DateTime.UtcNow < deadline);

        return items;
    }
}
