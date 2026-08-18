using AskLucy.Domain.Prompts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Prompts;

/// <summary>Proves <see cref="Prompt.RowVersion"/> (from <c>BaseEntity</c>) rejects a second concurrent edit rather than silently overwriting it (spec.md FR-007, research.md Decision 8) — no bespoke concurrency mechanism, just the platform-wide `RowVersion` convention.</summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class PromptConcurrencyTests(PersistenceTestFixture fixture)
{
    private static readonly PromptContentSnapshot InitialContent = new(
        "System instructions", null, "Summarize {{document}}.", null, null, null, null,
        null, null, null, null, false);

    [Fact]
    public async Task ApplyEdit_ShouldThrowConcurrencyException_WhenTheUnderlyingRowChangedSinceLoad()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var (prompt, _) = Prompt.Create(
            ownerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, InitialContent,
            [new PromptVariableDefinition("document", null, PromptVariableType.File, true, null, null, null, 0)], ownerId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));
            dbContext.Prompts.Add(prompt);
            await dbContext.SaveChangesAsync();
        }

        // Load the same row into two independent DbContext instances, simulating two browser tabs.
        await using var dbContextA = fixture.CreateDbContext();
        await using var dbContextB = fixture.CreateDbContext();

        var promptA = await dbContextA.Prompts.Include(p => p.Versions).ThenInclude(v => v.Variables).FirstAsync(p => p.Id == prompt.Id);
        var promptB = await dbContextB.Prompts.Include(p => p.Versions).ThenInclude(v => v.Variables).FirstAsync(p => p.Id == prompt.Id);

        promptA.ApplyEdit(InitialContent with { UserInstructions = "First edit {{document}}." },
            [new PromptVariableDefinition("document", null, PromptVariableType.File, true, null, null, null, 0)], null, ownerId);
        await dbContextA.SaveChangesAsync();

        promptB.ApplyEdit(InitialContent with { UserInstructions = "Second, conflicting edit {{document}}." },
            [new PromptVariableDefinition("document", null, PromptVariableType.File, true, null, null, null, 0)], null, ownerId);
        var act = () => dbContextB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
