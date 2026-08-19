using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>Resolves a registered <see cref="IWorkflowNodeExecutor"/> by <see cref="WorkflowNodeType"/> — mirrors <c>AgentToolCatalog.Find</c>'s DI-collection lookup shape (contracts/workflow-node-contract.md).</summary>
public sealed class WorkflowNodeExecutorRegistry(IEnumerable<IWorkflowNodeExecutor> executors)
{
    private readonly IReadOnlyDictionary<WorkflowNodeType, IWorkflowNodeExecutor> _executorsByType = executors.ToDictionary(e => e.NodeType);

    public IWorkflowNodeExecutor? Find(WorkflowNodeType nodeType) => _executorsByType.GetValueOrDefault(nodeType);
}
