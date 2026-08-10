using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Prompts;

/// <summary>
/// Proves <see cref="PromptRepository.SearchAsync"/>'s full-text query (against name/description/
/// system/user instructions) and its combined category+tag+folder filters (spec.md FR-052) against
/// a real SQL Server instance. Like <c>UserChatFullTextSearchTests</c>, SQL Server's FTS index
/// population is asynchronous, so full-text assertions poll until the expected row appears or a
/// deadline passes.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class PromptSearchTests(PersistenceTestFixture fixture)
{
    private static readonly PromptContentSnapshot BaseContent = new(
        "System instructions", null, "Summarize {{document}}.", null, null, null, null,
        null, null, null, null, false);

    private static readonly List<PromptVariableDefinition> Variables =
    [
        new("document", null, PromptVariableType.File, true, null, null, null, 0),
    ];

    [Fact]
    public async Task SearchAsync_ShouldMatchByName()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var (matching, _) = Prompt.Create(
            ownerId, "Quarterly budget narrative generator", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId);
        var (nonMatching, _) = Prompt.Create(
            ownerId, "Weekend trip planner", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId);

        await SeedAsync(ownerId, matching, nonMatching);

        var repository = new PromptRepository(fixture.CreateDbContext());
        var items = await SearchUntilAsync(repository, ownerId, "budget", results => results.Any(p => p.Id == matching.Id));

        items.Should().ContainSingle(p => p.Id == matching.Id);
    }

    [Fact]
    public async Task SearchAsync_ShouldMatchByDescription()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var (matching, _) = Prompt.Create(
            ownerId, $"Prompt {Guid.NewGuid():N}", "Extracts reimbursement policy clauses.", PromptType.Extraction, null, null,
            PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId);

        await SeedAsync(ownerId, matching);

        var repository = new PromptRepository(fixture.CreateDbContext());
        var items = await SearchUntilAsync(repository, ownerId, "reimbursement", results => results.Any(p => p.Id == matching.Id));

        items.Should().ContainSingle(p => p.Id == matching.Id);
    }

    [Fact]
    public async Task SearchAsync_ShouldMatchByUserInstructions()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var content = BaseContent with { UserInstructions = "Translate {{document}} preserving legal terminology." };
        var (matching, _) = Prompt.Create(
            ownerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Translation, null, null,
            PromptCapabilityRequirements.None, null, content, Variables, ownerId);

        await SeedAsync(ownerId, matching);

        var repository = new PromptRepository(fixture.CreateDbContext());
        var items = await SearchUntilAsync(repository, ownerId, "terminology", results => results.Any(p => p.Id == matching.Id));

        items.Should().ContainSingle(p => p.Id == matching.Id);
    }

    [Fact]
    public async Task SearchAsync_WithCombinedCategoryTagAndFolderFilters_ShouldReturnOnlyThePromptMatchingAllThree()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var category = PromptCategory.CreateCustom("Legal", ownerId, ownerId);
        var folder = PromptFolder.Create(ownerId, "Contracts", null, 0, 10, ownerId);

        var (matching, _) = Prompt.Create(
            ownerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Extraction, folder.Id, category.Id,
            PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId);
        matching.AddTag("nda", ownerId, ownerId);

        // Same category and tag, different folder — must be excluded by the folder filter.
        var (wrongFolder, _) = Prompt.Create(
            ownerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Extraction, null, category.Id,
            PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId);
        wrongFolder.AddTag("nda", ownerId, ownerId);

        // Same folder and tag, no category — must be excluded by the category filter.
        var (wrongCategory, _) = Prompt.Create(
            ownerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Extraction, folder.Id, null,
            PromptCapabilityRequirements.None, null, BaseContent, Variables, ownerId);
        wrongCategory.AddTag("nda", ownerId, ownerId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            dbContext.PromptCategories.Add(category);
            dbContext.PromptFolders.Add(folder);
            dbContext.Prompts.AddRange(matching, wrongFolder, wrongCategory);
            await dbContext.SaveChangesAsync();
        }

        var repository = new PromptRepository(fixture.CreateDbContext());
        var (items, _) = await repository.SearchAsync(
            ownerId, PromptListView.All, query: null, category.Id, "nda", folder.Id, status: null,
            cursor: null, pageSize: 50, CancellationToken.None);

        items.Should().ContainSingle(p => p.Id == matching.Id);
    }

    private async Task SeedAsync(string ownerId, params Prompt[] prompts)
    {
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
        dbContext.Prompts.AddRange(prompts);
        await dbContext.SaveChangesAsync();
    }

    // SQL Server FTS index population is asynchronous — poll the actual search behavior until the
    // expected row appears or the deadline passes, mirroring UserChatFullTextSearchTests.
    private static async Task<IReadOnlyList<Prompt>> SearchUntilAsync(
        PromptRepository repository, string ownerId, string searchTerm, Func<IReadOnlyList<Prompt>, bool> isReady)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        IReadOnlyList<Prompt> items = [];
        do
        {
            (items, _) = await repository.SearchAsync(
                ownerId, PromptListView.All, searchTerm, null, null, null, null, null, 50, CancellationToken.None);
            if (isReady(items))
            {
                return items;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        } while (DateTime.UtcNow < deadline);

        return items;
    }
}
