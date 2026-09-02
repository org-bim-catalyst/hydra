using AskLucy.Domain.Chats;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// The "memory analysis ran" stamp must survive the turn writing to the same conversation row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UserChat"/> carries a rowversion, and <c>MemoryExtractionJob</c> is enqueued at the
/// very end of a chat turn — the same moment the controller is still writing that row (the
/// assistant messages, and for a location query the resolved boundary). Stamping through the
/// tracked entity meant the job's rowversion was already stale by the time it saved, the UPDATE
/// matched zero rows, and Hangfire logged a <c>DbUpdateConcurrencyException</c>. Seen three times
/// on 2026-09-01, each immediately after a turn completed.
/// </para>
/// <para>
/// The first test below reproduces exactly that interleaving; it fails against a tracked-entity
/// save and passes against the targeted update.
/// </para>
/// </remarks>
[Collection(PersistenceTestCollection.Name)]
public sealed class MemoryAnalyzedStampTests(PersistenceTestFixture fixture)
{
    private async Task<Guid> SeedChatAsync(string userId)
    {
        var chat = UserChat.Create("Analysed chat", userId, null, userId);
        await using var seed = fixture.CreateDbContext();
        seed.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
        seed.UserChats.Add(chat);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        return chat.Id;
    }

    [Fact]
    public async Task MarkMemoryAnalyzedAsync_ShouldSucceed_EvenWhenTheChatWasWrittenSinceItWasLoaded()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chatId = await SeedChatAsync(userId);

        // The job's context, holding the rowversion as it was when the job started.
        await using var jobContext = fixture.CreateDbContext();
        var loadedByJob = await jobContext.UserChats.FirstAsync(c => c.Id == chatId, TestContext.Current.CancellationToken);
        loadedByJob.Should().NotBeNull();

        // Meanwhile the turn finishes and writes the same row, bumping its rowversion.
        await using (var turnContext = fixture.CreateDbContext())
        {
            var duringTurn = await turnContext.UserChats.FirstAsync(c => c.Id == chatId, TestContext.Current.CancellationToken);
            duringTurn.Rename("Renamed by the turn", userId);
            await turnContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var analyzedAt = DateTime.UtcNow;
        var act = async () => await new UserChatRepository(jobContext)
            .MarkMemoryAnalyzedAsync(chatId, analyzedAt, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync("the stamp is monotonic bookkeeping, not something a concurrent edit can corrupt");

        await using var verify = fixture.CreateDbContext();
        var reloaded = await verify.UserChats.FirstAsync(c => c.Id == chatId, TestContext.Current.CancellationToken);
        reloaded.LastMemoryAnalyzedAtUtc.Should().BeCloseTo(analyzedAt, TimeSpan.FromSeconds(5));
        reloaded.Title.Should().Be("Renamed by the turn", "the targeted update must touch only the timestamp");
    }

    [Fact]
    public async Task MarkMemoryAnalyzedAsync_ShouldTakeTheChatOutOfTheSweepQueue()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chatId = await SeedChatAsync(userId);

        await using var context = fixture.CreateDbContext();
        var repository = new UserChatRepository(context);

        var before = await repository.ListNeedingMemoryAnalysisAsync(50, TestContext.Current.CancellationToken);
        before.Select(c => c.Id).Should().Contain(chatId);

        await repository.MarkMemoryAnalyzedAsync(chatId, DateTime.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken);

        await using var verify = fixture.CreateDbContext();
        var after = await new UserChatRepository(verify).ListNeedingMemoryAnalysisAsync(50, TestContext.Current.CancellationToken);
        after.Select(c => c.Id).Should().NotContain(chatId, "MemoryExtractionSweepJob must not keep re-picking an analysed chat");
    }
}
