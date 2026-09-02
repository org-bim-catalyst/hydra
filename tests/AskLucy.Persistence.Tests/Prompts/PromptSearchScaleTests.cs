using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Prompts;

/// <summary>
/// Performance test for spec.md's SC-003 ("locate a specific prompt from a library of 1,000+
/// prompts, via search or filters, in under 10 seconds") — constitution &#167;10 requires this exist
/// and fail CI on regression past the documented threshold. Mirrors
/// <c>KnowledgeBaseScalePerformanceTests</c>'s seed-at-literal-scale approach.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class PromptSearchScaleTests(PersistenceTestFixture fixture)
{
    private const int PromptCount = 1_000;

    private static readonly PromptContentSnapshot BaseContent = new(
        "System instructions", null, "Summarize {{document}}.", null, null, null, null,
        null, null, null, null, false);

    private static readonly List<PromptVariableDefinition> Variables =
    [
        new("document", null, PromptVariableType.File, true, null, null, null, 0),
    ];

    [Fact]
    public async Task SearchAsync_FilteredByStatus_ShouldReturnAPage_InUnderTenSeconds_At1000Prompts()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var prompts = Enumerable.Range(0, PromptCount)
            .Select(i => Prompt.Create(
                ownerId, $"Prompt {i}", null, PromptType.Chat, null, null,
                PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId).Prompt)
            .ToList();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            seedContext.Prompts.AddRange(prompts);
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var dbContext = fixture.CreateDbContext();
        var repository = new PromptRepository(dbContext);

        var stopwatch = Stopwatch.StartNew();
        var (items, nextCursor) = await repository.SearchAsync(
            ownerId, PromptListView.All, query: null, categoryId: null, tag: null, folderId: null,
            status: PromptStatus.Active, cursor: null, pageSize: 50, CancellationToken.None);
        stopwatch.Stop();

        items.Should().HaveCount(50);
        nextCursor.Should().NotBeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "SC-003: locating a prompt via filters must complete in under 10s at 1,000+ prompts");
    }

    [Fact]
    public async Task SearchAsync_FullTextQuery_ShouldReturnMatches_InUnderTenSeconds_At1000Prompts()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var prompts = Enumerable.Range(0, PromptCount)
            .Select(i => Prompt.Create(
                ownerId, i % 25 == 0 ? $"Revit Schedule Extractor {i}" : $"Prompt {i}", null, PromptType.Extraction, null, null,
                PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId).Prompt)
            .ToList();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            seedContext.Prompts.AddRange(prompts);
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var repository = new PromptRepository(fixture.CreateDbContext());

        // SQL Server FTS index population is asynchronous (mirrors PromptSearchTests/
        // UserChatFullTextSearchTests) — the poll itself counts against the SC-003 budget below,
        // since a user's real search experience includes that same catalog-freshness lag.
        var stopwatch = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        IReadOnlyList<Prompt> items = [];
        do
        {
            (items, _) = await repository.SearchAsync(
                ownerId, PromptListView.All, "Revit", null, null, null, null, null, 50, CancellationToken.None);
            if (items.Count > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        } while (DateTime.UtcNow < deadline);
        stopwatch.Stop();

        items.Should().OnlyContain(p => p.Name.Contains("Revit"));
        items.Should().NotBeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "SC-003: locating a prompt via search must complete in under 10s at 1,000+ prompts");
    }
}
