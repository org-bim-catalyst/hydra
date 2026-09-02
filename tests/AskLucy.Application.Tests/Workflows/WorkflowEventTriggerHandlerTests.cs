using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.EventTriggers;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// spec.md User Story 9 (FR-063/FR-064/FR-070, Acceptance Scenarios 9.1-9.4) —
/// <see cref="WorkflowEventTriggerHandler"/> matches an event against every published Event-Driven
/// workflow's trigger scope, re-checks the WORKFLOW OWNER's current authorization (not the event's
/// own actor's), and enforces the same concurrency cap a manual start respects. "Starts within 1
/// minute" (SC-012) is a wall-clock property of the real background runner/Hangfire pipeline, not
/// something a unit test asserts directly — here, that translates to "the execution is created and
/// handed to <see cref="IWorkflowExecutionRunner.EnqueueAsync"/> synchronously within Handle."
/// </summary>
public sealed class WorkflowEventTriggerHandlerTests
{
    private const string OwnerId = "owner-1";
    private static readonly Guid KnowledgeBaseId = Guid.NewGuid();

    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IWorkflowExecutionRunner _runner = Substitute.For<IWorkflowExecutionRunner>();
    private readonly IWorkflowAuditLogRepository _auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public WorkflowEventTriggerHandlerTests()
    {
        _policyRepository.GetUserExecutionLimitAsync(OwnerId, Arg.Any<CancellationToken>()).Returns((WorkflowUserExecutionLimit?)null);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);
        _knowledgeBaseRepository.GetByIdAsync(KnowledgeBaseId, Arg.Any<CancellationToken>()).Returns(KnowledgeBase.Create("KB", OwnerId, OwnerId));
    }

    private WorkflowEventTriggerHandler CreateHandler() => new(
        _workflowRepository, _executionRepository, _policyRepository, _knowledgeBaseRepository, _runner, _auditLogRepository,
        Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()), _unitOfWork,
        Substitute.For<ILogger<WorkflowEventTriggerHandler>>());

    private (Workflow Workflow, WorkflowVersion Version) SetUpEventDrivenWorkflow(string eventTriggerConfigurationJson, WorkflowStatus status = WorkflowStatus.Published)
    {
        var workflow = Workflow.Create(OwnerId, "Triggered Workflow", null, WorkflowType.EventDriven, OwnerId);
        var version = workflow.Publish(
            [
                new WorkflowNodeSpec("start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
                new WorkflowNodeSpec("end", WorkflowNodeType.End, "End", null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0),
            ],
            [new WorkflowConnectionSpec("start", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        workflow.SetEventTriggerConfiguration(eventTriggerConfigurationJson, OwnerId);

        if (status == WorkflowStatus.Disabled)
        {
            workflow.Disable(OwnerId);
        }

        _workflowRepository.ListPublishedEventDrivenAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<(Workflow, WorkflowVersion)>)[(workflow, version)]);

        return (workflow, version);
    }

    [Fact]
    public async Task Handle_ShouldStartAnExecution_WhenTheDocumentUploadedEventMatchesTheTriggersScope()
    {
        SetUpEventDrivenWorkflow($$"""{"eventType":"DocumentUploaded","knowledgeBaseId":"{{KnowledgeBaseId}}"}""");

        await CreateHandler().Handle(new DocumentUploadedNotification(Guid.NewGuid(), KnowledgeBaseId, "uploader", "file.pdf"), CancellationToken.None);

        _executionRepository.Received(1).Add(Arg.Is<WorkflowExecution>(e => e != null && e.RunByUserId == OwnerId && e.TriggerType == WorkflowExecutionTriggerType.EventDriven));
        await _runner.Received(1).EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _auditLogRepository.Received(1).Add(Arg.Is<WorkflowAuditLog>(log => log != null && log.Action == WorkflowAuditAction.ExecutionStarted));
    }

    [Fact]
    public async Task Handle_ShouldNotStartAnExecution_WhenTheKnowledgeBaseScopeDoesNotMatch()
    {
        SetUpEventDrivenWorkflow($$"""{"eventType":"DocumentUploaded","knowledgeBaseId":"{{Guid.NewGuid()}}"}""");

        await CreateHandler().Handle(new DocumentUploadedNotification(Guid.NewGuid(), KnowledgeBaseId, "uploader", "file.pdf"), CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotStartAnExecution_WhenTheEventTypeDoesNotMatch()
    {
        SetUpEventDrivenWorkflow($$"""{"eventType":"KnowledgeBaseUpdated","knowledgeBaseId":"{{KnowledgeBaseId}}"}""");

        await CreateHandler().Handle(new DocumentUploadedNotification(Guid.NewGuid(), KnowledgeBaseId, "uploader", "file.pdf"), CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotStartAnExecution_AndShouldRecordPermissionDenied_WhenTheWorkflowOwnerNoLongerOwnsTheKnowledgeBase()
    {
        SetUpEventDrivenWorkflow($$"""{"eventType":"DocumentUploaded","knowledgeBaseId":"{{KnowledgeBaseId}}"}""");
        _knowledgeBaseRepository.GetByIdAsync(KnowledgeBaseId, Arg.Any<CancellationToken>()).Returns(KnowledgeBase.Create("KB", "someone-else", "someone-else"));

        await CreateHandler().Handle(new DocumentUploadedNotification(Guid.NewGuid(), KnowledgeBaseId, "uploader", "file.pdf"), CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _auditLogRepository.Received(1).Add(Arg.Is<WorkflowAuditLog>(log => log != null && log.Action == WorkflowAuditAction.PermissionDenied && log.ActorUserId == OwnerId));
    }

    [Fact]
    public async Task Handle_ShouldNotStartAnExecution_WhenTheOwnerIsAtTheirConcurrencyCap()
    {
        SetUpEventDrivenWorkflow($$"""{"eventType":"DocumentUploaded","knowledgeBaseId":"{{KnowledgeBaseId}}"}""");
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(3); // == WorkflowRuntimeOptions default

        await CreateHandler().Handle(new DocumentUploadedNotification(Guid.NewGuid(), KnowledgeBaseId, "uploader", "file.pdf"), CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNeverDispatch_ToAWorkflowThatIsNoLongerPublished()
    {
        // Defense-in-depth alongside IWorkflowRepository.ListPublishedEventDrivenAsync's own
        // Published-only filter (Acceptance Scenario 9.3) — even if a caller's mock/candidate set
        // includes a Disabled workflow, the handler itself never dispatches to it.
        SetUpEventDrivenWorkflow($$"""{"eventType":"DocumentUploaded","knowledgeBaseId":"{{KnowledgeBaseId}}"}""", WorkflowStatus.Disabled);

        await CreateHandler().Handle(new DocumentUploadedNotification(Guid.NewGuid(), KnowledgeBaseId, "uploader", "file.pdf"), CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldStartAnExecution_ForADocumentProcessedEvent_WithNoKnowledgeBaseScope()
    {
        SetUpEventDrivenWorkflow("""{"eventType":"DocumentProcessed"}""");

        await CreateHandler().Handle(new DocumentProcessedNotification(Guid.NewGuid(), OwnerId, "file.pdf"), CancellationToken.None);

        _executionRepository.Received(1).Add(Arg.Any<WorkflowExecution>());
        await _runner.Received(1).EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotStartAnExecution_ForADocumentProcessedEvent_TriggeredBySomeoneElsesDocument()
    {
        // DocumentProcessed has no knowledge-base scope to re-check ownership against, but it's
        // still owner-scoped: a workflow must never receive another user's processed-document
        // notification just because it declared no scope (the only configuration the UI offers
        // for this event type) — regression coverage for a real cross-tenant leak found in review.
        SetUpEventDrivenWorkflow("""{"eventType":"DocumentProcessed"}""");

        await CreateHandler().Handle(new DocumentProcessedNotification(Guid.NewGuid(), "someone-else", "someone-elses-file.pdf"), CancellationToken.None);

        _executionRepository.DidNotReceive().Add(Arg.Any<WorkflowExecution>());
        await _runner.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _auditLogRepository.Received(1).Add(Arg.Is<WorkflowAuditLog>(log => log != null && log.Action == WorkflowAuditAction.PermissionDenied));
    }

    [Fact]
    public async Task Handle_ShouldStartAnExecution_ForAKnowledgeBaseUpdatedEvent_ThatMatchesTheTriggersScope()
    {
        SetUpEventDrivenWorkflow($$"""{"eventType":"KnowledgeBaseUpdated","knowledgeBaseId":"{{KnowledgeBaseId}}"}""");

        await CreateHandler().Handle(new KnowledgeBaseUpdatedNotification(KnowledgeBaseId, "someone-else"), CancellationToken.None);

        _executionRepository.Received(1).Add(Arg.Is<WorkflowExecution>(e => e != null && e.RunByUserId == OwnerId));
        await _runner.Received(1).EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
