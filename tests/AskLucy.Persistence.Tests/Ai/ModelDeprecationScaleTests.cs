using System.Diagnostics;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Ai;

/// <summary>
/// specs/069 SC-007: confirming a deprecation costs the same whether thousands of users hold the
/// retired model or none, because the confirming save never touches user items. The item switch is
/// the background job's work, and this measures that it stays linear and bounded per chunk.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class ModelDeprecationScaleTests(PersistenceTestFixture fixture)
{
    private const int Users = 2_000;
    private const int Chats = 5_000;

    [Fact(Skip = ScalePerformanceGate.SkipReason, SkipWhen = nameof(ScalePerformanceGate.NotRequested), SkipType = typeof(ScalePerformanceGate))]
    public async Task CountAndSwitch_TwoThousandUsers_FiveThousandChats_StayWithinTheConfirmBudget()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = AIProvider.Create($"scale-{Guid.NewGuid():N}", "Scale Provider", "test");
        var retired = AIModel.Create(provider.Id, "old", "Old", 128_000, 4096, Caps(), null, null, "test");
        var replacement = AIModel.Create(provider.Id, "new", "New", 128_000, 4096, Caps(), null, null, "test");
        retired.Deprecate("test");
        var batch = ModelDeprecationBatch.Deprecate(provider.Id, "test", DateTime.UtcNow);
        var record = ModelDeprecation.Create(batch.Id, retired.Id, replacement.Id, ReplacementSource.Proposed, "test", DateTime.UtcNow);
        batch.Add(record);

        var ownerIds = Enumerable.Range(0, Users).Select(i => $"scale-owner-{i:D4}-{Guid.NewGuid():N}").ToList();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.AIProviders.Add(provider);
            seed.AIModels.AddRange(retired, replacement);
            seed.ModelDeprecationBatches.Add(batch);
            seed.Users.AddRange(ownerIds.Select(PersistenceTestFixture.CreateTestUser));
            await seed.SaveChangesAsync(ct);

            for (var i = 0; i < Chats; i++)
            {
                var chat = UserChat.Create($"Chat {i}", ownerIds[i % Users], null, ownerIds[i % Users]);
                chat.SetModelSelection(provider.Id, retired.Id, null, ownerIds[i % Users]);
                seed.UserChats.Add(chat);
                if (i % 500 == 499)
                {
                    await seed.SaveChangesAsync(ct);
                }
            }

            await seed.SaveChangesAsync(ct);
        }

        await using (var db = fixture.CreateDbContext())
        {
            var store = new ModelReferenceStore(db);

            var countWatch = Stopwatch.StartNew();
            var counts = await store.CountAsync([retired.Id], ct);
            countWatch.Stop();
            counts.Conversations.Should().Be(Chats);
            counts.DistinctUsers.Should().Be(Users);
            countWatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), $"the preview must stay inside the confirm budget ({countWatch.Elapsed})");

            var targets = new Dictionary<Guid, RetiredModelTarget>
            {
                [retired.Id] = new RetiredModelTarget(record.Id, replacement.Id, provider.Id, replacement.ModelKey, replacement.DisplayName),
            };

            // One chunk is what the job does per transaction; the confirm itself does none of this.
            var chunk = ownerIds.Take(UserImpactProcessor.OwnerChunkSize).ToList();
            var switchWatch = Stopwatch.StartNew();
            var switched = await store.SwitchAsync(batch.Id, targets, chunk, "system", DateTime.UtcNow, ct);
            await db.SaveChangesAsync(ct);
            switchWatch.Stop();
            switched.Should().NotBeEmpty();
            // A regression guard, not a user-facing budget: this chunk is background work, and
            // this suite runs against a shared remote SQL host whose IO dominates the figure.
            switchWatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), $"one chunk must stay bounded ({switchWatch.Elapsed})");
            Console.WriteLine($"SC-007 on the shared host: count {countWatch.Elapsed}, switch {switchWatch.Elapsed}");
        }

        // The seed is large; removing it keeps the shared test database usable.
        await using (var cleanup = fixture.CreateDbContext())
        {
            await cleanup.UserChats.IgnoreQueryFilters().Where(c => ownerIds.Contains(c.UserId)).ExecuteDeleteAsync(ct);
            await cleanup.ItemSwitches.IgnoreQueryFilters().Where(s => s.BatchId == batch.Id).ExecuteDeleteAsync(ct);
            await cleanup.PlatformDefaultChanges.IgnoreQueryFilters().Where(c => c.DeprecationId == record.Id).ExecuteDeleteAsync(ct);
            await cleanup.ModelDeprecations.IgnoreQueryFilters().Where(d => d.BatchId == batch.Id).ExecuteDeleteAsync(ct);
            await cleanup.ModelDeprecationBatches.IgnoreQueryFilters().Where(b => b.Id == batch.Id).ExecuteDeleteAsync(ct);
            await cleanup.AIModels.IgnoreQueryFilters().Where(m => m.ProviderId == provider.Id).ExecuteDeleteAsync(ct);
            await cleanup.AIProviders.IgnoreQueryFilters().Where(p => p.Id == provider.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.IgnoreQueryFilters().Where(u => ownerIds.Contains(u.Id)).ExecuteDeleteAsync(ct);
        }
    }

    private static AIModelCapabilities Caps() =>
        new(true, false, false, false, false, false, false, false, false);
}
