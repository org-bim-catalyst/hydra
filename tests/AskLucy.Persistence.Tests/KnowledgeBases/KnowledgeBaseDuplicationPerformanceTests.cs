using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.DuplicateKnowledgeBase;
using AskLucy.Application.Options;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Persistence.Tests.KnowledgeBases;

/// <summary>
/// Performance test for spec.md's SC-006 ("duplicate ... in under 10 seconds for knowledge
/// bases with up to 1,000 documents") — constitution &#167;10. <see cref="IFileStorage"/> is
/// mocked (returns instantly) rather than backed by a real filesystem: the guarantee under
/// test here is the database-write cost of the deep copy (N document/folder inserts in one
/// transaction), not incidental disk I/O, which `LocalFileStorage`'s own tests already cover.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class KnowledgeBaseDuplicationPerformanceTests(PersistenceTestFixture fixture)
{
    private const int DocumentCount = 1_000;

    [Fact]
    public async Task Handle_ShouldDuplicateAKnowledgeBaseWith1000Documents_InUnderTenSeconds()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var source = KnowledgeBase.Create("Large Knowledge Base", ownerId, ownerId);
        var documents = Enumerable.Range(0, DocumentCount)
            .Select(i => KnowledgeBaseDocument.Create(source.Id, null, $"doc-{i}.txt", $"stored-doc-{i}.txt", "text/plain", 1024, null, ownerId))
            .ToList();
        foreach (var document in documents)
        {
            source.ApplyDocumentAdded(document.PageCount, document.SizeBytes, ownerId);
        }

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            seedContext.KnowledgeBases.Add(source);
            seedContext.KnowledgeBaseDocuments.AddRange(documents);
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var dbContext = fixture.CreateDbContext();
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new MemoryStream());
        fileStorage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult($"copy-{Guid.NewGuid():N}"));
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(ownerId);

        var handler = new DuplicateKnowledgeBaseCommandHandler(
            new KnowledgeBaseRepository(dbContext),
            new KnowledgeBaseFolderRepository(dbContext),
            new KnowledgeBaseDocumentRepository(dbContext),
            new KnowledgeBaseAuditLogRepository(dbContext),
            fileStorage,
            Options.Create(new KnowledgeBaseFolderOptions()),
            new KnowledgeBaseDashboardSummaryCache(new MemoryCache(new MemoryCacheOptions())),
            new UnitOfWork(dbContext),
            currentUser);

        var stopwatch = Stopwatch.StartNew();
        var duplicate = await handler.Handle(new DuplicateKnowledgeBaseCommand(source.Id), CancellationToken.None);
        stopwatch.Stop();

        duplicate.DocumentCount.Should().Be(DocumentCount);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "SC-006: duplicating a knowledge base with up to 1,000 documents must complete in under 10s");
    }
}
