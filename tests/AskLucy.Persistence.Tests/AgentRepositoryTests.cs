using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests;

/// <summary>specs/047 FR-001 — <see cref="AgentRepository.ListSystemOwnedAsync"/> against a real database.</summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class AgentRepositoryTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task ListSystemOwnedAsync_ShouldReturnOnlySystemOwnedAgents_ExcludingUserOwnedOnes()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerId = $"user-{suffix}";
        var systemAgent = Agent.CreateSystemProvisioned(
            $"lucy.orchestrator-{suffix}", $"Lucy-{suffix}", description: null, AgentType.Conversational,
            AgentInstructions.Empty, AiCapability.Chat, AgentExecutionPolicy.Empty, actor: "system");
        systemAgent.PublishSystemVersion(["resolve_location"], definitionHash: "hash-1", actor: "system");

        var userAgent = Agent.Create(
            ownerId, $"My Agent-{suffix}", description: null, AgentType.Task,
            AgentInstructions.Empty, modelProviderId: null, modelId: null, AgentOutputFormat.Markdown,
            AgentExecutionPolicy.Empty, actor: ownerId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            // Agents.OwnerId carries a real foreign key to AspNetUsers (AgentConfiguration.cs) —
            // both the system pseudo-account (Agent.SystemOwnerId, shared/idempotent across test
            // runs) and this test's own throwaway user must exist as real rows first.
            if (await dbContext.Users.FindAsync([Agent.SystemOwnerId], TestContext.Current.CancellationToken) is null)
            {
                dbContext.Users.Add(new ApplicationUser
                {
                    Id = Agent.SystemOwnerId,
                    UserName = "system@asklucy.internal",
                    Email = "system@asklucy.internal",
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }

            dbContext.Users.Add(new ApplicationUser
            {
                Id = ownerId,
                UserName = $"{ownerId}@example.com",
                Email = $"{ownerId}@example.com",
                CreatedAtUtc = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            dbContext.Agents.AddRange(systemAgent, userAgent);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new AgentRepository(readContext);

        var result = await repository.ListSystemOwnedAsync(TestContext.Current.CancellationToken);

        result.Should().Contain(a => a.Id == systemAgent.Id);
        result.Should().NotContain(a => a.Id == userAgent.Id);
        result.Single(a => a.Id == systemAgent.Id).Versions.Should().ContainSingle(v => v.VersionNumber == 1);
    }
}
