using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Commands.PublishWorkflowVersion;
using AskLucy.Application.Workflows.Commands.StartWorkflowExecution;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Validation;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// T096 — a published <see cref="WorkflowVersion"/> is immutable, and an execution always
/// references the exact version it was started against, even after a newer version publishes
/// (spec.md User Story 3, FR-014, Independent Test).
/// </summary>
public sealed class WorkflowVersioningTests
{
    private const string OwnerId = "user-1";

    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowExecutionRepository _executionRepository = Substitute.For<IWorkflowExecutionRepository>();
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();
    private readonly IWorkflowExecutionRunner _runner = Substitute.For<IWorkflowExecutionRunner>();
    private readonly IWorkflowAuditLogRepository _auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly WorkflowGraphValidator _validator = new(new WorkflowExpressionEvaluator());

    private static string LinearDraftJson(string endConfigurationJson) =>
        $$"""
        {
          "errorPolicyJson": "{\"strategy\":\"Stop\"}",
          "nodes": [
            {"nodeKey":"start","nodeType":"Start","name":"Start","description":null,"inputSchemaJson":"{}","outputSchemaJson":"{}","configurationJson":"{}","requiredPermissionsJson":"[]","timeoutSeconds":null,"retryPolicyJson":null,"approvalPolicy":"NeverRequire","idempotencyKeyExpression":null,"compensatingNodeKey":null,"canvasX":0,"canvasY":0},
            {"nodeKey":"end","nodeType":"End","name":"End","description":null,"inputSchemaJson":"{}","outputSchemaJson":"{}","configurationJson":{{endConfigurationJson}},"requiredPermissionsJson":"[]","timeoutSeconds":null,"retryPolicyJson":null,"approvalPolicy":"NeverRequire","idempotencyKeyExpression":null,"compensatingNodeKey":null,"canvasX":0,"canvasY":0}
          ],
          "connections": [{"sourceNodeKey":"start","targetNodeKey":"end","branchLabel":null,"typeContract":null}],
          "variables": []
        }
        """;

    private PublishWorkflowVersionCommandHandler CreatePublishHandler() => new(_workflowRepository, _validator, _auditLogRepository, _unitOfWork, _currentUser);

    private StartWorkflowExecutionCommandHandler CreateStartHandler() => new(
        _workflowRepository, _executionRepository, _policyRepository, _runner, _auditLogRepository,
        Microsoft.Extensions.Options.Options.Create(new WorkflowRuntimeOptions()), _unitOfWork, _currentUser);

    private Workflow SetUpWorkflow(string draftDefinitionJson)
    {
        _currentUser.UserId.Returns(OwnerId);
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        workflow.UpdateDraft(workflow.Name, workflow.Description, draftDefinitionJson, OwnerId);
        _workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        return workflow;
    }

    [Fact]
    public async Task PublishingASecondVersion_ShouldNotMutate_TheFirstVersionsFrozenNodes()
    {
        var workflow = SetUpWorkflow(LinearDraftJson("\"{}\""));

        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v1"), CancellationToken.None);
        var v1 = workflow.Versions.Single(v => v.VersionNumber == 1);
        v1.Nodes.Single(n => n.NodeKey == "end").ConfigurationJson.Should().Be("{}");

        workflow.UpdateDraft(workflow.Name, workflow.Description, LinearDraftJson("\"{\\\"outputs\\\":{\\\"x\\\":1}}\""), OwnerId);
        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v2"), CancellationToken.None);

        workflow.PublishedVersionNumber.Should().Be(2);
        var v1AfterV2Publish = workflow.Versions.Single(v => v.VersionNumber == 1);
        var v2 = workflow.Versions.Single(v => v.VersionNumber == 2);

        v1AfterV2Publish.Nodes.Single(n => n.NodeKey == "end").ConfigurationJson.Should().Be("{}");
        v2.Nodes.Single(n => n.NodeKey == "end").ConfigurationJson.Should().Be("{\"outputs\":{\"x\":1}}");
    }

    [Fact]
    public async Task AnExecutionStartedAgainstAnOlderVersion_ShouldKeepReferencingIt_AfterANewerVersionPublishes()
    {
        var workflow = SetUpWorkflow(LinearDraftJson("\"{}\""));
        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v1"), CancellationToken.None);
        var v1 = workflow.Versions.Single(v => v.VersionNumber == 1);

        _workflowRepository.GetVersionAsync(workflow.Id, 1, Arg.Any<CancellationToken>()).Returns(v1);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        WorkflowExecution? capturedExecution = null;
        _executionRepository.When(r => r.Add(Arg.Any<WorkflowExecution>())).Do(call => capturedExecution = call.Arg<WorkflowExecution>());

        var executionSummary = await CreateStartHandler().Handle(
            new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        capturedExecution.Should().NotBeNull();
        capturedExecution!.WorkflowVersionId.Should().Be(v1.Id);

        // Publishing v2 must not retroactively change what the already-created execution references.
        workflow.UpdateDraft(workflow.Name, workflow.Description, LinearDraftJson("\"{\\\"outputs\\\":{\\\"x\\\":1}}\""), OwnerId);
        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v2"), CancellationToken.None);

        capturedExecution.WorkflowVersionId.Should().Be(v1.Id);
        capturedExecution.WorkflowVersionId.Should().NotBe(workflow.Versions.Single(v => v.VersionNumber == 2).Id);
        executionSummary.Should().NotBeNull();
    }

    [Fact]
    public async Task ANewExecutionWithNoExplicitVersion_ShouldDefaultToTheCurrentlyPublishedVersion_NotAnOlderOne()
    {
        var workflow = SetUpWorkflow(LinearDraftJson("\"{}\""));
        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v1"), CancellationToken.None);

        workflow.UpdateDraft(workflow.Name, workflow.Description, LinearDraftJson("\"{\\\"outputs\\\":{\\\"x\\\":1}}\""), OwnerId);
        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v2"), CancellationToken.None);
        var v2 = workflow.Versions.Single(v => v.VersionNumber == 2);

        _workflowRepository.GetVersionAsync(workflow.Id, 2, Arg.Any<CancellationToken>()).Returns(v2);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        await CreateStartHandler().Handle(new StartWorkflowExecutionCommand(workflow.Id, null, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        _executionRepository.Received(1).Add(Arg.Is<WorkflowExecution>(e => e.WorkflowVersionId == v2.Id));
    }

    [Fact]
    public async Task ANewExecution_CanExplicitlyTargetAnOlderVersion_EvenAfterANewerOneIsPublished()
    {
        var workflow = SetUpWorkflow(LinearDraftJson("\"{}\""));
        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v1"), CancellationToken.None);
        var v1 = workflow.Versions.Single(v => v.VersionNumber == 1);

        workflow.UpdateDraft(workflow.Name, workflow.Description, LinearDraftJson("\"{\\\"outputs\\\":{\\\"x\\\":1}}\""), OwnerId);
        await CreatePublishHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v2"), CancellationToken.None);

        _workflowRepository.GetVersionAsync(workflow.Id, 1, Arg.Any<CancellationToken>()).Returns(v1);
        _executionRepository.CountActiveByUserAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(0);

        await CreateStartHandler().Handle(new StartWorkflowExecutionCommand(workflow.Id, 1, "{}", WorkflowExecutionTriggerType.Manual), CancellationToken.None);

        _executionRepository.Received(1).Add(Arg.Is<WorkflowExecution>(e => e.WorkflowVersionId == v1.Id));
    }
}
