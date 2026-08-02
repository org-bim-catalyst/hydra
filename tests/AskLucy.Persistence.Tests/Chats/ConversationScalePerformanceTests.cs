using System.Diagnostics;
using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Domain.Chats;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// Performance test for the scale targets named in plan.md's Technical Context and
/// spec.md's Success Criteria — constitution &#167;10 requires this exist and fail CI on
/// regression past the documented threshold for any path with a stated performance goal.
///
/// Message-count scale is reduced from spec.md's literal "hundreds of thousands" (SC-003)
/// to a seed size this test can populate in CI in a reasonable time; the shape of the query
/// (single indexed keyset page fetch) does not change with row count, so this remains a
/// faithful regression guard for the query plan even at a smaller seeded scale. The
/// conversation count (SC-001) is seeded at its literal stated value.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class ConversationScalePerformanceTests(PersistenceTestFixture fixture)
{
    private const int ConversationCount = 10_000;
    private const int MessageCountInLargeConversation = 20_000;

    [Fact]
    public async Task SearchAsync_ShouldReturnAPage_InUnderThreeSeconds_At10000Conversations()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chats = Enumerable.Range(0, ConversationCount)
            .Select(i => UserChat.Create($"Conversation {i}", userId, null, userId))
            .ToList();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            seedContext.UserChats.AddRange(chats);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = fixture.CreateDbContext();
        var repository = new UserChatRepository(dbContext);

        var stopwatch = Stopwatch.StartNew();
        var (items, nextCursor) = await repository.SearchAsync(
            userId, ConversationView.Active, null, null, null, ConversationSort.Alphabetical, null, pageSize: 50, CancellationToken.None);
        stopwatch.Stop();

        items.Should().HaveCount(50);
        nextCursor.Should().NotBeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "SC-001: search/filter must return in under 3s at 10,000+ conversations");
    }

    [Fact]
    public async Task ListPagedByChatIdAsync_ShouldReturnAPage_Quickly_ForAVeryLongConversation()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Long-running conversation", userId, null, userId);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            seedContext.UserChats.Add(chat);
            for (var batchStart = 0; batchStart < MessageCountInLargeConversation; batchStart += 1000)
            {
                var batch = Enumerable.Range(batchStart, Math.Min(1000, MessageCountInLargeConversation - batchStart))
                    .Select(i => Message.Create(chat.Id, MessageRole.User, MessageKind.Text, $"Message {i}", null, userId));
                seedContext.Messages.AddRange(batch);
                await seedContext.SaveChangesAsync();
            }
        }

        await using var dbContext = fixture.CreateDbContext();
        var repository = new MessageRepository(dbContext);

        var stopwatch = Stopwatch.StartNew();
        var (items, _) = await repository.ListPagedByChatIdAsync(chat.Id, cursor: null, pageSize: 50, CancellationToken.None);
        stopwatch.Stop();

        items.Should().HaveCount(50);
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(1), "SC-003: message-list scroll/loading must stay responsive regardless of total conversation length");
    }
}
