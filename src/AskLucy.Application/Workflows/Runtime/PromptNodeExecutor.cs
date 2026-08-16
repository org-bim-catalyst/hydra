using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Resolves a saved Prompt (variables substituted via <see cref="PromptVariableResolver"/>) and
/// generates through the existing <see cref="IAIProvider"/> abstraction — never a direct provider
/// SDK call (research.md Decision 2, contracts/workflow-node-contract.md). RAG/Memory context
/// augmentation is intentionally not duplicated here: an author composes an explicit RAG Search /
/// Memory Search node ahead of this one and references its output instead. Configuration shape:
/// <c>{"promptId": "...", "versionNumber": (optional), "providerId": "...", "modelId": "...",
/// "variableValues": {"name": "literal or {{...}}"}, "outputField": "text" (optional)}</c>.
/// </summary>
public sealed class PromptNodeExecutor(
    IPromptRepository promptRepository,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IAIProviderResolver providerResolver,
    IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.AiPrompt;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (!TryGetGuid(root, "promptId", out var promptId) || !TryGetGuid(root, "providerId", out var providerId) || !TryGetGuid(root, "modelId", out var modelId))
        {
            return WorkflowNodeExecutionResult.Failure("AI Prompt node configuration requires 'promptId', 'providerId', and 'modelId'.");
        }

        var prompt = await promptRepository.GetByIdForOwnerAsync(promptId, context.UserId, cancellationToken);
        if (prompt is null)
        {
            return WorkflowNodeExecutionResult.Failure("The configured prompt was not found, or is not owned by the workflow's initiating user.");
        }

        var versionNumber = root.TryGetProperty("versionNumber", out var versionElement) && versionElement.ValueKind == JsonValueKind.Number
            ? versionElement.GetInt32()
            : prompt.CurrentVersionNumber;
        var version = await promptRepository.GetVersionAsync(prompt.Id, versionNumber, cancellationToken);
        if (version is null)
        {
            return WorkflowNodeExecutionResult.Failure("The configured prompt version was not found.");
        }

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);
        var suppliedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("variableValues", out var variableValuesElement) && variableValuesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in variableValuesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!WorkflowCapabilityToolInvoker.TryResolveConfigString(property.Value.GetString(), expressionEvaluator, resolvedValues, out var value, out var expressionError))
                {
                    return WorkflowNodeExecutionResult.Failure(expressionError!);
                }

                suppliedValues[property.Name] = value;
            }
        }

        var resolution = PromptVariableResolver.ValidateAndResolve(version.Variables, suppliedValues);
        if (!resolution.IsValid)
        {
            return WorkflowNodeExecutionResult.Failure("Prompt variable validation failed: " + string.Join("; ", resolution.Errors.Select(e => e.Message)));
        }

        var provider = await providerRepository.GetByIdAsync(providerId, cancellationToken);
        var model = await modelRepository.GetByIdAsync(modelId, cancellationToken);
        if (provider is null || model is null)
        {
            return WorkflowNodeExecutionResult.Failure("The configured AI provider or model was not found.");
        }

        var resolvedSystem = PromptVariableResolver.ResolveContent(version.SystemInstructions, resolution.ResolvedValues);
        var resolvedDeveloper = PromptVariableResolver.ResolveContent(version.DeveloperInstructions, resolution.ResolvedValues);
        var resolvedUser = PromptVariableResolver.ResolveContent(version.UserInstructions, resolution.ResolvedValues);

        var messages = new List<ChatMessage>();
        var combinedSystem = string.Join("\n\n", new[] { resolvedSystem, resolvedDeveloper }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(combinedSystem))
        {
            messages.Add(new ChatMessage(ChatRole.System, combinedSystem));
        }

        messages.Add(new ChatMessage(ChatRole.User, resolvedUser));

        ChatCompletionResult completion;
        try
        {
            var aiProvider = providerResolver.Resolve(provider.ProviderKey);
            completion = await aiProvider.ChatAsync(messages, model.ModelKey, parameters: null, cancellationToken);
        }
        catch (Exception ex) when (ex is AiProviderUnavailableException or AiProviderAuthenticationException or AiProviderRateLimitedException)
        {
            return WorkflowNodeExecutionResult.Failure($"AI provider call failed: {ex.Message}");
        }

        var outputField = root.TryGetProperty("outputField", out var outputFieldElement) && outputFieldElement.ValueKind == JsonValueKind.String
            ? outputFieldElement.GetString()!
            : "text";

        var output = WorkflowResolvedValues.ToInputDocument(new Dictionary<string, WorkflowExpressionValue> { [outputField] = WorkflowExpressionValue.OfString(completion.Content) });
        return WorkflowNodeExecutionResult.Success(output);
    }

    private static bool TryGetGuid(JsonElement root, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out value);
    }
}
