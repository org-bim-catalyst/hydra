using System.Text.Json;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Combines a Parallel node's branch outputs per an explicit strategy (FR-031). Configuration
/// shape: <c>{"strategy": "AllCompleted"|"FirstCompleted"|"AnyCompleted"|"CollectAll",
/// "branchNodeKeys": ["rag","memory"]}</c>. By the time <see cref="WorkflowExecutionOrchestrator"/>
/// invokes this executor, the branches applicable to the configured strategy have already been
/// flattened into <paramref name="input"/> under <c>steps.&lt;branchNodeKey&gt;.*</c>, exactly
/// like any other completed node's output (the orchestrator owns the actual
/// <see cref="System.Threading.Tasks.Task.WhenAll{TResult}(System.Threading.Tasks.Task{TResult}[])"/>/
/// <see cref="System.Threading.Tasks.Task.WhenAny(System.Threading.Tasks.Task[])"/> waiting, since
/// only it manages the concurrent branch <c>Task</c>s — an <see cref="IWorkflowNodeExecutor"/> has
/// no way to receive them through its fixed single-<c>input</c>-document signature).
///
/// <para><b>Output shape</b>: the expression engine's closed value model (research.md Decision 6)
/// has no nested-object/array-of-object type, so a true JSON array for <c>CollectAll</c> isn't
/// representable. <c>AllCompleted</c>/<c>CollectAll</c> both produce one flat output keyed
/// <c>"&lt;branchNodeKey&gt;.&lt;field&gt;"</c> per branch (matching this engine's existing
/// flattened-dotted-key convention everywhere else); <c>FirstCompleted</c>/<c>AnyCompleted</c>
/// produce the single winning branch's fields directly, plus a <c>"branch"</c> field naming it.</para>
/// </summary>
public sealed class MergeNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Merge;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        var strategy = root.TryGetProperty("strategy", out var strategyElement) && strategyElement.ValueKind == JsonValueKind.String
            ? strategyElement.GetString()!
            : "AllCompleted";

        var branchNodeKeys = root.TryGetProperty("branchNodeKeys", out var keysElement) && keysElement.ValueKind == JsonValueKind.Array
            ? keysElement.EnumerateArray().Select(e => e.GetString()).Where(k => k is not null).Cast<string>().ToList()
            : [];

        if (branchNodeKeys.Count == 0)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure("Merge node configuration is missing a required non-empty 'branchNodeKeys' array."));
        }

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);

        return Task.FromResult(strategy switch
        {
            "AllCompleted" or "CollectAll" => CombineAll(resolvedValues, branchNodeKeys),
            "FirstCompleted" or "AnyCompleted" => CombineWinner(resolvedValues, branchNodeKeys),
            _ => WorkflowNodeExecutionResult.Failure($"Unknown merge strategy '{strategy}'."),
        });
    }

    private static WorkflowNodeExecutionResult CombineAll(IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues, IReadOnlyList<string> branchNodeKeys)
    {
        var output = new Dictionary<string, WorkflowExpressionValue>(StringComparer.Ordinal);
        foreach (var branchNodeKey in branchNodeKeys)
        {
            foreach (var (field, value) in ExtractBranchFields(resolvedValues, branchNodeKey))
            {
                output[$"{branchNodeKey}.{field}"] = value;
            }
        }

        if (output.Count == 0)
        {
            return WorkflowNodeExecutionResult.Failure("None of the configured branches produced any output to merge.");
        }

        return WorkflowNodeExecutionResult.Success(WorkflowResolvedValues.ToInputDocument(output));
    }

    private static WorkflowNodeExecutionResult CombineWinner(IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues, IReadOnlyList<string> branchNodeKeys)
    {
        var winner = branchNodeKeys.FirstOrDefault(key => ExtractBranchFields(resolvedValues, key).Count > 0);
        if (winner is null)
        {
            return WorkflowNodeExecutionResult.Failure("None of the configured branches completed successfully.");
        }

        var output = new Dictionary<string, WorkflowExpressionValue>(StringComparer.Ordinal) { ["branch"] = WorkflowExpressionValue.OfString(winner) };
        foreach (var (field, value) in ExtractBranchFields(resolvedValues, winner))
        {
            output[field] = value;
        }

        return WorkflowNodeExecutionResult.Success(WorkflowResolvedValues.ToInputDocument(output));
    }

    private static Dictionary<string, WorkflowExpressionValue> ExtractBranchFields(IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues, string branchNodeKey)
    {
        var prefix = $"steps.{branchNodeKey}.";
        return resolvedValues
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value, StringComparer.Ordinal);
    }
}
