using AskLucy.Application.KnowledgeBases;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.KnowledgeBases;

/// <summary>
/// Proves cursor (keyset) pagination for <see cref="KnowledgeBaseRepository.SearchAsync"/>
/// returns stable, non-duplicated, non-skipped pages — including across a pinned/unpinned
/// boundary and when a new row is inserted between page loads (constitution &#167;6, mirrors
/// `Chats/CursorPaginationTests.cs`).
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class KnowledgeBaseCursorPaginationTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task SearchAsync_ShouldPageThroughAllKnowledgeBases_WithoutDuplicatesOrGaps()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var knowledgeBases = Enumerable.Range(0, 5)
            .Select(i => KnowledgeBase.Create($"KB {i}", ownerId, ownerId))
            .ToList();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            dbContext.KnowledgeBases.AddRange(knowledgeBases);
            await dbContext.SaveChangesAsync();
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var dbContext = fixture.CreateDbContext();
            var repository = new KnowledgeBaseRepository(dbContext);
            var (items, nextCursor) = await repository.SearchAsync(
                ownerId, KnowledgeBaseListView.Active, null, null, null, null, null,
                KnowledgeBaseSort.Name, sortDescending: false, cursor, pageSize: 2, CancellationToken.None);

            seen.AddRange(items.Select(k => k.Id));
            cursor = nextCursor;
        } while (cursor is not null);

        seen.Should().BeEquivalentTo(knowledgeBases.Select(k => k.Id), options => options.WithoutStrictOrdering());
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SearchAsync_ShouldNotDuplicateOrSkip_WhenANewKnowledgeBaseIsInsertedBetweenPages()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var knowledgeBases = Enumerable.Range(0, 3)
            .Select(i => KnowledgeBase.Create($"KB {i}", ownerId, ownerId))
            .ToList();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            dbContext.KnowledgeBases.AddRange(knowledgeBases);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new KnowledgeBaseRepository(readContext);

        var (firstPage, cursor) = await repository.SearchAsync(
            ownerId, KnowledgeBaseListView.Active, null, null, null, null, null,
            KnowledgeBaseSort.Name, sortDescending: false, null, pageSize: 2, CancellationToken.None);

        var insertedLate = KnowledgeBase.Create("ZZZ inserted late", ownerId, ownerId);
        await using (var writeContext = fixture.CreateDbContext())
        {
            writeContext.KnowledgeBases.Add(insertedLate);
            await writeContext.SaveChangesAsync();
        }

        var (secondPage, _) = await repository.SearchAsync(
            ownerId, KnowledgeBaseListView.Active, null, null, null, null, null,
            KnowledgeBaseSort.Name, sortDescending: false, cursor, pageSize: 10, CancellationToken.None);

        firstPage.Select(k => k.Id).Intersect(secondPage.Select(k => k.Id)).Should().BeEmpty("no row should appear twice across pages");
        secondPage.Should().Contain(k => k.Id == insertedLate.Id);
        secondPage.Should().Contain(k => k.Id == knowledgeBases[2].Id, "the third original knowledge base must not be skipped");
    }

    [Fact]
    public async Task SearchAsync_ShouldAlwaysReturnPinnedKnowledgeBases_BeforeUnpinnedOnes_AcrossPageBoundaries()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var pinned = Enumerable.Range(0, 2).Select(i => KnowledgeBase.Create($"Pinned {i}", ownerId, ownerId)).ToList();
        var unpinned = Enumerable.Range(0, 3).Select(i => KnowledgeBase.Create($"Unpinned {i}", ownerId, ownerId)).ToList();
        foreach (var knowledgeBase in pinned)
        {
            knowledgeBase.Pin(ownerId);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            dbContext.KnowledgeBases.AddRange(pinned);
            dbContext.KnowledgeBases.AddRange(unpinned);
            await dbContext.SaveChangesAsync();
        }

        var seenInOrder = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var dbContext = fixture.CreateDbContext();
            var repository = new KnowledgeBaseRepository(dbContext);
            var (items, nextCursor) = await repository.SearchAsync(
                ownerId, KnowledgeBaseListView.Active, null, null, null, null, null,
                KnowledgeBaseSort.Name, sortDescending: false, cursor, pageSize: 2, CancellationToken.None);

            seenInOrder.AddRange(items.Select(k => k.Id));
            cursor = nextCursor;
        } while (cursor is not null);

        var pinnedIds = pinned.Select(k => k.Id).ToHashSet();
        var firstUnpinnedIndex = seenInOrder.FindIndex(id => !pinnedIds.Contains(id));
        seenInOrder.Take(firstUnpinnedIndex).Should().OnlyContain(id => pinnedIds.Contains(id), "FR-028: pinned knowledge bases must sort before unpinned ones");
        seenInOrder.Should().OnlyHaveUniqueItems();
        seenInOrder.Should().HaveCount(5);
    }

    [Fact]
    public async Task SearchAsync_ShouldPageThroughByDocumentCount_WithoutDuplicatesOrGaps()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var knowledgeBases = Enumerable.Range(0, 4).Select(i => KnowledgeBase.Create($"KB {i}", ownerId, ownerId)).ToList();
        for (var i = 0; i < knowledgeBases.Count; i++)
        {
            for (var d = 0; d < i; d++)
            {
                knowledgeBases[i].ApplyDocumentAdded(pageCount: 1, sizeBytes: 1024, actor: ownerId);
            }
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            dbContext.KnowledgeBases.AddRange(knowledgeBases);
            await dbContext.SaveChangesAsync();
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var dbContext = fixture.CreateDbContext();
            var repository = new KnowledgeBaseRepository(dbContext);
            var (items, nextCursor) = await repository.SearchAsync(
                ownerId, KnowledgeBaseListView.Active, null, null, null, null, null,
                KnowledgeBaseSort.DocumentCount, sortDescending: true, cursor, pageSize: 2, CancellationToken.None);

            seen.AddRange(items.Select(k => k.Id));
            cursor = nextCursor;
        } while (cursor is not null);

        seen.Should().BeEquivalentTo(knowledgeBases.Select(k => k.Id), options => options.WithoutStrictOrdering());
        seen.Should().OnlyHaveUniqueItems();
    }
}
