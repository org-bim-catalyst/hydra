using System.Text.Json.Nodes;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Prompts;
using AskLucy.Domain.Workflows;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Ai;

/// <summary>
/// specs/069 FR-012/FR-014 research D10/D11: counts and rewrites only live user items, never
/// published versions, and edits the workflow draft's AiPrompt node without touching the rest.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class ModelReferenceStoreTests(PersistenceTestFixture fixture)
{
    private const string DraftWithPromptNode = """
        {"nodes":[
          {"nodeKey":"start","nodeType":"Start","configurationJson":"{}"},
          {"nodeKey":"ask","nodeType":"AiPrompt","configurationJson":"{\"promptId\":\"{PROMPT}\",\"providerId\":\"{PROVIDER}\",\"modelId\":\"{MODEL}\"}"},
          {"nodeKey":"end","nodeType":"End","configurationJson":"{}"}
        ],"connections":[],"variables":[]}
        """;

    private static readonly PromptContentSnapshot Content = new(
        "System", null, "Answer {{question}}.", null, null, null, null, null, null, null, null, false);

    private static string Draft(Guid promptId, Guid providerId, Guid modelId) =>
        DraftWithPromptNode
            .Replace("{PROMPT}", promptId.ToString(), StringComparison.Ordinal)
            .Replace("{PROVIDER}", providerId.ToString(), StringComparison.Ordinal)
            .Replace("{MODEL}", modelId.ToString(), StringComparison.Ordinal);

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task CountAndSwitch_CoverEveryLiveItem_AndLeaveDeletedAndVersionRowsAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = $"owner-{Guid.NewGuid():N}";
        var provider = AIProvider.Create($"prov-{Guid.NewGuid():N}", "Provider", owner);
        var retired = AIModel.Create(provider.Id, "old", "Old Model", 128_000, 4096,
            new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, owner);
        var replacement = AIModel.Create(provider.Id, "new", "New Model", 128_000, 4096,
            new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, owner);
        retired.Deprecate(owner);

        var chat = UserChat.Create("Chat", owner, null, owner);
        chat.SetModelSelection(provider.Id, retired.Id, null, owner);
        var deletedChat = UserChat.Create("Deleted chat", owner, null, owner);
        deletedChat.SetModelSelection(provider.Id, retired.Id, null, owner);
        deletedChat.SoftDelete(owner);

        var (prompt, _) = Prompt.Create(
            owner, "Prompt", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None,
            "old", Content, [new PromptVariableDefinition("question", null, PromptVariableType.String, true, null, null, null, 0)], owner);

        var agent = Agent.Create(
            owner, "Agent", null, AgentType.Task, AgentInstructions.Empty,
            provider.Id, retired.Id, AgentOutputFormat.PlainText, new AgentExecutionPolicy(null, null, null, null, null, null), owner);

        var workflow = Workflow.Create(owner, "Flow", null, WorkflowType.Manual, owner);
        workflow.UpdateDraft(workflow.Name, null, Draft(prompt.Id, provider.Id, retired.Id), owner);

        var preference = UserAiPreference.Create(owner, owner);
        preference.SetDefaults(provider.Id, retired.Id, null, owner);

        var targets = new Dictionary<Guid, RetiredModelTarget>();
        var batch = ModelDeprecationBatch.Deprecate(provider.Id, owner, DateTime.UtcNow);
        var record = ModelDeprecation.Create(batch.Id, retired.Id, replacement.Id, ReplacementSource.Proposed, owner, DateTime.UtcNow);
        batch.Add(record);
        targets[retired.Id] = new RetiredModelTarget(record.Id, replacement.Id, provider.Id, replacement.ModelKey, replacement.DisplayName);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(owner));
            dbContext.AIProviders.Add(provider);
            dbContext.AIModels.AddRange(retired, replacement);
            dbContext.UserChats.AddRange(chat, deletedChat);
            dbContext.Prompts.Add(prompt);
            dbContext.Agents.Add(agent);
            dbContext.Workflows.Add(workflow);
            dbContext.UserAiPreferences.Add(preference);
            dbContext.ModelDeprecationBatches.Add(batch);
            await dbContext.SaveChangesAsync(ct);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var store = new ModelReferenceStore(dbContext);
            var ids = new[] { retired.Id };

            var counts = await store.CountAsync(ids, ct);
            counts.Conversations.Should().Be(1);
            counts.Agents.Should().Be(1);
            counts.Prompts.Should().Be(1);
            counts.WorkflowSteps.Should().Be(1);
            counts.DistinctUsers.Should().Be(1);

            var switched = await store.SwitchAsync(batch.Id, targets, [owner], "system", DateTime.UtcNow, ct);
            switched.Should().ContainSingle().Which.Items.Should().HaveCount(5);
            await dbContext.SaveChangesAsync(ct);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.UserChats.Single(c => c.Id == chat.Id).ModelId.Should().Be(replacement.Id);
            dbContext.UserChats.Single(c => c.Id == deletedChat.Id).ModelId.Should().Be(retired.Id);
            dbContext.Agents.Single(a => a.Id == agent.Id).ModelId.Should().Be(replacement.Id);
            dbContext.Prompts.Single(p => p.Id == prompt.Id).PreferredModelKey.Should().Be(replacement.ModelKey);
            dbContext.UserAiPreferences.Single(p => p.UserId == owner).DefaultModelId.Should().Be(replacement.Id);
            dbContext.ItemSwitches.Count(s => s.BatchId == batch.Id).Should().Be(5);

            var draft = JsonNode.Parse(dbContext.Workflows.Single(w => w.Id == workflow.Id).DraftDefinitionJson)!;
            var ask = draft["nodes"]!.AsArray().Single(n => n!["nodeKey"]!.GetValue<string>() == "ask")!;
            var config = JsonNode.Parse(ask["configurationJson"]!.GetValue<string>())!;
            config["modelId"]!.GetValue<string>().Should().Be(replacement.Id.ToString());
            config["providerId"]!.GetValue<string>().Should().Be(provider.Id.ToString());
            draft["nodes"]!.AsArray().Should().HaveCount(3);

            // A second pass finds nothing left to switch and records nothing new.
            var store = new ModelReferenceStore(dbContext);
            var again = await store.SwitchAsync(batch.Id, targets, [owner], "system", DateTime.UtcNow, ct);
            again.Should().BeEmpty();
        }
    }
}
