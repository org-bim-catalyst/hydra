using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Tests.Workflows;
using AskLucy.Application.Workflows.Commands.PauseWorkflowExecution;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.Workflows;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications.Emitters;

/// <summary>T076 (specs/067) — <see cref="WorkflowExecutionOrchestrator"/>'s started/completed/failed publishes, and <see cref="PauseWorkflowExecutionCommandHandler"/>'s paused publish.</summary>
public sealed class WorkflowExecutionNotificationTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();
    private readonly WorkflowBudgetGuard _budgetGuard = new(Microsoft.Extensions.Options.Options.Create(new Options.WorkflowRuntimeOptions()));
    private readonly WorkflowPolicyEvaluator _policyEvaluator = new(Substitute.For<IWorkflowPolicyRepository>());
    private readonly AgentToolCatalog _toolCatalog = new([], WorkflowOrchestratorTestHelpers.EmptyMcpToolRegistry());
    private readonly INotificationPublisher _notificationPublisher = Substitute.For<INotificationPublisher>();

    private static WorkflowNodeSpec Node(string key, WorkflowNodeType type, string configurationJson = "{}") => new(
        key, type, key, null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

    private WorkflowExecutionOrchestrator CreateOrchestrator(WorkflowNodeExecutorRegistry registry) => new(
        _executionRepository, _workflowRepository, registry, _expressionEvaluator, _budgetGuard, _policyEvaluator, _toolCatalog,
        WorkflowOrchestratorTestHelpers.NoOpNotifier(), WorkflowOrchestratorTestHelpers.NoOpAuditLogRepository(), _notificationPublisher, _unitOfWork);

    private (WorkflowExecution Execution, Workflow Workflow, WorkflowVersion Version) SetUpLinearWorkflow()
    {
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [
                Node("start", WorkflowNodeType.Start),
                Node("transform", WorkflowNodeType.Transform, "{\"expression\":\"{{workflow.text}}\",\"outputField\":\"result\"}"),
                Node("end", WorkflowNodeType.End, "{\"outputs\":{\"result\":\"{{steps.transform.result}}\"}}"),
            ],
            [
                new WorkflowConnectionSpec("start", "transform", null, null),
                new WorkflowConnectionSpec("transform", "end", null, null),
            ],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{\"text\":\"hello\"}", OwnerId);

        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _workflowRepository.GetByIdAsync(workflow.Id, Arg.Any<CancellationToken>()).Returns(workflow);

        return (execution, workflow, version);
    }

    private static WorkflowNodeExecutorRegistry RegistryWithTransform(IWorkflowExpressionEvaluator evaluator) => new([new TransformNodeExecutor(evaluator)]);

    [Fact]
    public async Task RunAsync_ShouldPublishStartedThenCompleted_WhenTheWorkflowSucceeds()
    {
        var (execution, workflow, _) = SetUpLinearWorkflow();

        await CreateOrchestrator(RegistryWithTransform(_expressionEvaluator)).RunAsync(execution.Id, TestContext.Current.CancellationToken);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            r!.Type == NotificationTypeKeys.WorkflowExecutionStarted &&
            ((NotificationRecipient.User)r.Recipient).UserId == OwnerId &&
            r.RelatedItem == new RelatedItem("WorkflowExecution", execution.Id.ToString(), workflow.Id.ToString()) &&
            r.EventKey == $"workflow-execution:{execution.Id}:started"));
        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            r!.Type == NotificationTypeKeys.WorkflowExecutionCompleted &&
            r.EventKey == $"workflow-execution:{execution.Id}:completed"));
    }

    [Fact]
    public async Task RunAsync_ShouldPublishFailed_WhenANodeHasNoRegisteredExecutor()
    {
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("rag", WorkflowNodeType.RagSearch), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "rag", null, null), new WorkflowConnectionSpec("rag", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        var execution = WorkflowExecution.Create(workflow.Id, version.Id, OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        _executionRepository.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        _workflowRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _workflowRepository.GetByIdAsync(workflow.Id, Arg.Any<CancellationToken>()).Returns(workflow);

        await CreateOrchestrator(new WorkflowNodeExecutorRegistry([])).RunAsync(execution.Id, TestContext.Current.CancellationToken);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            r!.Type == NotificationTypeKeys.WorkflowExecutionFailed &&
            r.EventKey == $"workflow-execution:{execution.Id}:failed"));
    }

    [Fact]
    public async Task PauseWorkflowExecution_ShouldPublishWorkflowExecutionPaused_Once()
    {
        var (execution, workflow, _) = SetUpLinearWorkflow();
        execution.Start();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        _executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var sut = new PauseWorkflowExecutionCommandHandler(
            _executionRepository, _workflowRepository, WorkflowOrchestratorTestHelpers.NoOpNotifier(), _notificationPublisher, _unitOfWork, currentUser);

        await sut.Handle(new PauseWorkflowExecutionCommand(execution.Id), TestContext.Current.CancellationToken);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            r!.Type == NotificationTypeKeys.WorkflowExecutionPaused &&
            r.RelatedItem == new RelatedItem("WorkflowExecution", execution.Id.ToString(), workflow.Id.ToString()) &&
            r.EventKey == $"workflow-execution:{execution.Id}:paused"));
    }

    [Fact]
    public async Task PauseWorkflowExecution_ShouldPublishNothing_WhenTheExecutionIsNotRunning()
    {
        var (execution, _, _) = SetUpLinearWorkflow();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(OwnerId);
        _executionRepository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        var sut = new PauseWorkflowExecutionCommandHandler(
            _executionRepository, _workflowRepository, WorkflowOrchestratorTestHelpers.NoOpNotifier(), _notificationPublisher, _unitOfWork, currentUser);

        await sut.Handle(new PauseWorkflowExecutionCommand(execution.Id), TestContext.Current.CancellationToken);

        _notificationPublisher.DidNotReceiveWithAnyArgs().Publish(default!);
    }
}
