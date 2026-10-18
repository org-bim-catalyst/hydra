using System.Text.Json;
using System.Text.Json.Nodes;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/069 FR-012/FR-014 research D10/D11: the places an end-user item names a model.
/// Query filters exclude soft-deleted rows, so deleted items are never counted or rewritten.
/// Published versions and messages are history and are never touched (research D3).
/// </summary>
public sealed class ModelReferenceStore(AskLucyDbContext dbContext) : IModelReferenceStore
{
    private const string AiPromptNodeType = "AiPrompt";

    public async Task<ModelImpactCounts> CountAsync(IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default)
    {
        if (modelIds.Count == 0)
        {
            return ModelImpactCounts.Empty;
        }

        var keyById = await RetiredModelsAsync(modelIds, cancellationToken);
        var modelKeys = keyById.Values.Select(m => m.Key).ToList();
        var owners = new HashSet<string>(StringComparer.Ordinal);

        var conversationOwners = await dbContext.UserChats.Where(c => c.ModelId != null && modelIds.Contains(c.ModelId.Value))
            .Select(c => c.UserId).Distinct().ToListAsync(cancellationToken);
        var conversations = await dbContext.UserChats.CountAsync(c => c.ModelId != null && modelIds.Contains(c.ModelId.Value), cancellationToken);

        var agentOwners = await dbContext.Agents.Where(a => a.ModelId != null && modelIds.Contains(a.ModelId.Value))
            .Select(a => a.OwnerId).Distinct().ToListAsync(cancellationToken);
        var agents = await dbContext.Agents.CountAsync(a => a.ModelId != null && modelIds.Contains(a.ModelId.Value), cancellationToken);

        var promptOwners = await dbContext.Prompts.Where(p => p.PreferredModelKey != null && modelKeys.Contains(p.PreferredModelKey))
            .Select(p => p.OwnerId).Distinct().ToListAsync(cancellationToken);
        var prompts = await dbContext.Prompts.CountAsync(p => p.PreferredModelKey != null && modelKeys.Contains(p.PreferredModelKey), cancellationToken);

        var workflows = await CandidateWorkflowsAsync(modelIds, cancellationToken);
        var workflowSteps = workflows.Sum(w => CountAiPromptNodes(w.DraftDefinitionJson, modelIds));

        var preferenceOwners = await dbContext.UserAiPreferences
            .Where(p => p.DefaultModelId != null && modelIds.Contains(p.DefaultModelId.Value))
            .Select(p => p.UserId).Distinct().ToListAsync(cancellationToken);

        owners.UnionWith(conversationOwners);
        owners.UnionWith(agentOwners);
        owners.UnionWith(promptOwners);
        owners.UnionWith(preferenceOwners);
        owners.UnionWith(workflows.Select(w => w.OwnerId));

        return new ModelImpactCounts(conversations, agents, prompts, workflowSteps, owners.Count);
    }

    public async Task<IReadOnlyList<string>> ListOwnerIdsAsync(IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default)
    {
        if (modelIds.Count == 0)
        {
            return [];
        }

        var keyById = await RetiredModelsAsync(modelIds, cancellationToken);
        var modelKeys = keyById.Values.Select(m => m.Key).ToList();
        var owners = new HashSet<string>(StringComparer.Ordinal);

        owners.UnionWith(await dbContext.UserChats.Where(c => c.ModelId != null && modelIds.Contains(c.ModelId.Value))
            .Select(c => c.UserId).Distinct().ToListAsync(cancellationToken));
        owners.UnionWith(await dbContext.Agents.Where(a => a.ModelId != null && modelIds.Contains(a.ModelId.Value))
            .Select(a => a.OwnerId).Distinct().ToListAsync(cancellationToken));
        owners.UnionWith(await dbContext.Prompts.Where(p => p.PreferredModelKey != null && modelKeys.Contains(p.PreferredModelKey))
            .Select(p => p.OwnerId).Distinct().ToListAsync(cancellationToken));
        owners.UnionWith(await dbContext.UserAiPreferences
            .Where(p => p.DefaultModelId != null && modelIds.Contains(p.DefaultModelId.Value))
            .Select(p => p.UserId).Distinct().ToListAsync(cancellationToken));
        owners.UnionWith((await CandidateWorkflowsAsync(modelIds, cancellationToken)).Select(w => w.OwnerId));

        return owners.OrderBy(o => o, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<OwnerSwitchResult>> SwitchAsync(
        Guid batchId,
        IReadOnlyDictionary<Guid, RetiredModelTarget> replacements,
        IReadOnlyList<string> ownerIds,
        string actor,
        DateTime atUtc,
        CancellationToken cancellationToken = default)
    {
        if (replacements.Count == 0 || ownerIds.Count == 0)
        {
            return [];
        }

        var modelIds = replacements.Keys.ToHashSet();
        var keyById = await RetiredModelsAsync(modelIds, cancellationToken);
        var idByKey = keyById.ToDictionary(kv => kv.Value.Key, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        var switchedByOwner = new Dictionary<string, List<OwnerSwitchItem>>(StringComparer.Ordinal);
        var records = new List<ItemSwitch>();

        void Add(string owner, Guid retiredId, string label, ModelImpactKind kind, Guid itemId, string? nodeId)
        {
            var target = replacements[retiredId];
            if (!switchedByOwner.TryGetValue(owner, out var list))
            {
                switchedByOwner[owner] = list = [];
            }

            list.Add(new OwnerSwitchItem(
                retiredId,
                keyById.TryGetValue(retiredId, out var retired) ? retired.DisplayName : "A retired model",
                target.ModelId, target.DisplayName, label, kind));
            records.Add(ItemSwitch.Create(
                batchId, target.DeprecationId, owner,
                kind switch
                {
                    ModelImpactKind.Conversation => SwitchedItemKind.Conversation,
                    ModelImpactKind.Agent => SwitchedItemKind.Agent,
                    ModelImpactKind.Prompt => SwitchedItemKind.Prompt,
                    ModelImpactKind.WorkflowStep => SwitchedItemKind.WorkflowStep,
                    _ => SwitchedItemKind.UserDefault,
                },
                itemId, nodeId, label, retiredId, target.ModelId, atUtc));
        }

        foreach (var chat in await dbContext.UserChats
                     .Where(c => ownerIds.Contains(c.UserId) && c.ModelId != null && modelIds.Contains(c.ModelId.Value))
                     .ToListAsync(cancellationToken))
        {
            var retiredId = chat.ModelId!.Value;
            var target = replacements[retiredId];
            chat.SetModelSelection(target.ProviderId, target.ModelId, chat.GenerationParametersJson, actor);
            Add(chat.UserId, retiredId, chat.Title, ModelImpactKind.Conversation, chat.Id, null);
        }

        foreach (var agent in await dbContext.Agents
                     .Where(a => ownerIds.Contains(a.OwnerId) && a.ModelId != null && modelIds.Contains(a.ModelId.Value))
                     .ToListAsync(cancellationToken))
        {
            var retiredId = agent.ModelId!.Value;
            var target = replacements[retiredId];
            agent.SetModel(target.ProviderId, target.ModelId, actor);
            Add(agent.OwnerId, retiredId, agent.Name, ModelImpactKind.Agent, agent.Id, null);
        }

        if (idByKey.Count > 0)
        {
            var retiredKeys = idByKey.Keys.ToList();
            foreach (var prompt in await dbContext.Prompts
                         .Where(p => ownerIds.Contains(p.OwnerId) && p.PreferredModelKey != null && retiredKeys.Contains(p.PreferredModelKey))
                         .ToListAsync(cancellationToken))
            {
                var retiredId = idByKey[prompt.PreferredModelKey!];
                prompt.SetPreferredModel(replacements[retiredId].ModelKey, actor);
                Add(prompt.OwnerId, retiredId, prompt.Name, ModelImpactKind.Prompt, prompt.Id, null);
            }
        }

        foreach (var workflow in (await CandidateWorkflowsAsync(modelIds, cancellationToken)).Where(w => ownerIds.Contains(w.OwnerId)))
        {
            var (json, switched) = RewriteDraft(workflow.DraftDefinitionJson, replacements);
            if (switched.Count == 0)
            {
                continue;
            }

            workflow.UpdateDraft(workflow.Name, workflow.Description, json, actor);
            foreach (var (retiredId, nodeKey) in switched)
            {
                Add(workflow.OwnerId, retiredId, $"{workflow.Name} · {nodeKey}", ModelImpactKind.WorkflowStep, workflow.Id, nodeKey);
            }
        }

        foreach (var preference in await dbContext.UserAiPreferences
                     .Where(p => ownerIds.Contains(p.UserId) && p.DefaultModelId != null && modelIds.Contains(p.DefaultModelId.Value))
                     .ToListAsync(cancellationToken))
        {
            var retiredId = preference.DefaultModelId!.Value;
            var target = replacements[retiredId];
            preference.SetDefaults(target.ProviderId, target.ModelId, preference.DefaultGenerationParametersJson, actor);
            Add(preference.UserId, retiredId, "Default model", ModelImpactKind.UserDefault, preference.Id, null);
        }

        dbContext.ItemSwitches.AddRange(records);
        return switchedByOwner.Select(kv => new OwnerSwitchResult(kv.Key, kv.Value)).ToList();
    }

    /// <summary>Retired model id → its vendor key (null key when the row is gone).</summary>
    private async Task<Dictionary<Guid, (string Key, string DisplayName, Guid ProviderId)>> RetiredModelsAsync(
        IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken) =>
        await dbContext.AIModels.Where(m => modelIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ModelKey, m.DisplayName, m.ProviderId })
            .ToDictionaryAsync(m => m.Id, m => (m.ModelKey, m.DisplayName, m.ProviderId), cancellationToken);

    private async Task<List<Workflow>> CandidateWorkflowsAsync(IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken)
    {
        if (modelIds.Count == 0)
        {
            return [];
        }

        // Drafts are parsed in memory: a LIKE per model id cannot be translated from a
        // collection, and the platform's draft count is small (research D10). The tracked
        // entities are needed so a confirmed rewrite can be saved by the caller.
        var drafts = await dbContext.Workflows.ToListAsync(cancellationToken);

        return drafts.Where(w => CountAiPromptNodes(w.DraftDefinitionJson, modelIds) > 0).ToList();
    }

    private static int CountAiPromptNodes(string draftJson, IReadOnlyCollection<Guid> modelIds) =>
        RewriteDraft(draftJson, modelIds.ToDictionary(id => id, id => new RetiredModelTarget(Guid.Empty, id, Guid.Empty, string.Empty, string.Empty)))
            .Switched.Count;

    /// <summary>Rewrites each AiPrompt node's <c>configurationJson</c> model/provider ids; everything else is preserved.</summary>
    private static (string Json, List<(Guid RetiredId, string NodeKey)> Switched) RewriteDraft(
        string draftJson, IReadOnlyDictionary<Guid, RetiredModelTarget> replacements)
    {
        var switched = new List<(Guid, string)>();
        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return (draftJson, switched);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(draftJson);
        }
        catch (JsonException)
        {
            return (draftJson, switched);
        }

        if (root?["nodes"] is not JsonArray nodes)
        {
            return (draftJson, switched);
        }

        foreach (var node in nodes.OfType<JsonObject>())
        {
            if (node["nodeType"]?.GetValue<string>() != AiPromptNodeType
                || node["configurationJson"]?.GetValue<string>() is not { } configJson
                || node["nodeKey"]?.GetValue<string>() is not { } nodeKey)
            {
                continue;
            }

            JsonObject? config;
            try
            {
                config = JsonNode.Parse(configJson) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }

            if (config?["modelId"]?.GetValue<string>() is not { } modelIdText
                || !Guid.TryParse(modelIdText, out var retiredId)
                || !replacements.TryGetValue(retiredId, out var target))
            {
                continue;
            }

            config["modelId"] = target.ModelId.ToString();
            if (config["providerId"] is not null)
            {
                config["providerId"] = target.ProviderId.ToString();
            }

            node["configurationJson"] = config.ToJsonString();
            switched.Add((retiredId, nodeKey));
        }

        return (switched.Count == 0 ? draftJson : root.ToJsonString(), switched);
    }
}
