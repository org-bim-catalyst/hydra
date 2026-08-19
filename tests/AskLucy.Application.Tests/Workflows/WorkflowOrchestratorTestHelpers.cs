using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using NSubstitute;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>Shared fixtures for orchestrator-level tests that need an <see cref="AgentToolCatalog"/> wired up (for the approval gate's underlying-tool risk lookup) but aren't testing capability nodes themselves.</summary>
internal static class WorkflowOrchestratorTestHelpers
{
    public static IMcpToolRegistry EmptyMcpToolRegistry()
    {
        var registry = Substitute.For<IMcpToolRegistry>();
        registry.ActiveTools.Returns((IReadOnlyCollection<IAgentTool>)[]);
        return registry;
    }

    /// <summary>A no-op <see cref="IWorkflowExecutionNotifier"/> for tests that don't assert on live-push behavior — every method returns a completed <see cref="Task"/> by NSubstitute's default.</summary>
    public static IWorkflowExecutionNotifier NoOpNotifier() => Substitute.For<IWorkflowExecutionNotifier>();

    /// <summary>A no-op <see cref="IWorkflowAuditLogRepository"/> for tests that don't assert on audit-log writes.</summary>
    public static IWorkflowAuditLogRepository NoOpAuditLogRepository() => Substitute.For<IWorkflowAuditLogRepository>();
}
