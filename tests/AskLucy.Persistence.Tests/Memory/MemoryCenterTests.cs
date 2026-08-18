using AskLucy.Domain.Memory;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Tests.Memory;

/// <summary>
/// tasks.md T045 (US2 AC1–AC4) — Memory Center list/search/pagination filtering, edit history,
/// and delete's immediate exclusion, proved against a real SQL Server instance (constitution
/// §10) rather than an in-memory approximation, mirroring `KnowledgeBaseCursorPaginationTests`.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class MemoryCenterTests(PersistenceTestFixture fixture)
{
    private static MemoryEntity CreateActive(string userId, MemoryCategory category, string content) =>
        MemoryEntity.CreateCandidate(
            userId, null, category, content, MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

    [Fact]
    public async Task SearchAsync_ShouldFilterByCategoryAndState_AndPaginateWithoutDuplicatesOrGaps()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var preferences = Enumerable.Range(0, 3).Select(i => CreateActive(userId, MemoryCategory.UserPreference, $"Preference {i}")).ToList();
        var facts = Enumerable.Range(0, 2).Select(i => CreateActive(userId, MemoryCategory.PersonalFact, $"Fact {i}")).ToList();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.Memories.AddRange(preferences);
            dbContext.Memories.AddRange(facts);
            await dbContext.SaveChangesAsync();
        }

        var seen = new List<Guid>();
        Guid? afterId = null;
        do
        {
            await using var dbContext = fixture.CreateDbContext();
            var repository = new MemoryRepository(dbContext);
            var page = await repository.SearchAsync(
                userId, MemoryCategory.UserPreference, MemoryLifecycleState.Active, null, generalOnly: false,
                query: null, afterId, pageSize: 2, CancellationToken.None);

            seen.AddRange(page.Select(m => m.Id));
            afterId = page.Count == 2 ? page[^1].Id : null;
        } while (afterId is not null);

        seen.Should().BeEquivalentTo(preferences.Select(m => m.Id), options => options.WithoutStrictOrdering());
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task CountAsync_ShouldMatchSearchAsyncsFilters_IgnoringPageSize()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var memories = Enumerable.Range(0, 5).Select(i => CreateActive(userId, MemoryCategory.PersonalFact, $"Fact {i}")).ToList();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.Memories.AddRange(memories);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new MemoryRepository(readContext);
        var count = await repository.CountAsync(
            userId, MemoryCategory.PersonalFact, null, null, generalOnly: false, query: null, CancellationToken.None);

        count.Should().Be(5);
    }

    [Fact]
    public async Task SearchAsync_ShouldFindByFreeTextContent_DespiteContentBeingEncryptedAtRest()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var target = CreateActive(userId, MemoryCategory.PersonalFact, "Works on BIM coordination for a mechanical contractor");
        var other = CreateActive(userId, MemoryCategory.PersonalFact, "Prefers dark mode");

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.Memories.AddRange(target, other);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new MemoryRepository(readContext);
        var results = await repository.SearchAsync(
            userId, null, null, null, generalOnly: false, query: "mechanical contractor", null, pageSize: 50, CancellationToken.None);

        results.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task Edit_ShouldAppendAMemoryVersion_RetrievableViaMemoryVersionRepository()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var memory = CreateActive(userId, MemoryCategory.PersonalFact, "Original content");

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.Memories.Add(memory);
            await dbContext.SaveChangesAsync();
        }

        await using (var writeContext = fixture.CreateDbContext())
        {
            var memoryRepository = new MemoryRepository(writeContext);
            var versionRepository = new MemoryVersionRepository(writeContext);
            var loaded = await memoryRepository.GetByIdAsync(memory.Id, CancellationToken.None) ?? throw new InvalidOperationException();

            var previousContent = loaded.Edit("Corrected content", userId);
            versionRepository.Add(MemoryVersion.Create(loaded.Id, previousContent, MemoryChangeReason.UserEdit, userId));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var reloadedMemory = await new MemoryRepository(readContext).GetByIdAsync(memory.Id, CancellationToken.None);
        var history = await new MemoryVersionRepository(readContext).GetByMemoryIdAsync(memory.Id, CancellationToken.None);

        reloadedMemory!.Content.Should().Be("Corrected content");
        history.Should().ContainSingle(v => v.PreviousContent == "Original content" && v.ChangeReason == MemoryChangeReason.UserEdit);
    }

    [Fact]
    public async Task Delete_ShouldExcludeTheMemory_FromEveryFutureSearchAsyncCall()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var memory = CreateActive(userId, MemoryCategory.PersonalFact, "To be deleted");

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.Memories.Add(memory);
            await dbContext.SaveChangesAsync();
        }

        await using (var writeContext = fixture.CreateDbContext())
        {
            var repository = new MemoryRepository(writeContext);
            var loaded = await repository.GetByIdAsync(memory.Id, CancellationToken.None) ?? throw new InvalidOperationException();
            loaded.SoftDelete(userId);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var results = await new MemoryRepository(readContext).SearchAsync(
            userId, null, null, null, generalOnly: false, query: null, null, pageSize: 50, CancellationToken.None);

        results.Should().NotContain(m => m.Id == memory.Id);
    }
}
