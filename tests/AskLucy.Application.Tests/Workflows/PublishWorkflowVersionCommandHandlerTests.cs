using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Commands.PublishWorkflowVersion;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Validation;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

public sealed class PublishWorkflowVersionCommandHandlerTests
{
    private const string OwnerId = "user-1";
    private const string ValidDraftJson =
        """
        {
          "errorPolicyJson": "{\"strategy\":\"Stop\"}",
          "nodes": [
            {"nodeKey":"start","nodeType":"Start","name":"Start","description":null,"inputSchemaJson":"{}","outputSchemaJson":"{}","configurationJson":"{}","requiredPermissionsJson":"[]","timeoutSeconds":null,"retryPolicyJson":null,"approvalPolicy":"NeverRequire","idempotencyKeyExpression":null,"compensatingNodeKey":null,"canvasX":0,"canvasY":0},
            {"nodeKey":"end","nodeType":"End","name":"End","description":null,"inputSchemaJson":"{}","outputSchemaJson":"{}","configurationJson":"{}","requiredPermissionsJson":"[]","timeoutSeconds":null,"retryPolicyJson":null,"approvalPolicy":"NeverRequire","idempotencyKeyExpression":null,"compensatingNodeKey":null,"canvasX":0,"canvasY":0}
          ],
          "connections": [{"sourceNodeKey":"start","targetNodeKey":"end","branchLabel":null,"typeContract":null}],
          "variables": []
        }
        """;

    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowAuditLogRepository _auditLogRepository = Substitute.For<IWorkflowAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly WorkflowGraphValidator _validator = new(new WorkflowExpressionEvaluator());

    private PublishWorkflowVersionCommandHandler CreateHandler() => new(_workflowRepository, _validator, _auditLogRepository, _unitOfWork, _currentUser);

    private Workflow SetUpWorkflow(string draftDefinitionJson)
    {
        _currentUser.UserId.Returns(OwnerId);
        var workflow = Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Manual, OwnerId);
        workflow.UpdateDraft("My Workflow", null, draftDefinitionJson, OwnerId);
        _workflowRepository.GetByIdForOwnerAsync(workflow.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(workflow);
        return workflow;
    }

    [Fact]
    public async Task Handle_ShouldMaterializeTheDraftIntoAnImmutableVersion_AndPublishTheWorkflow()
    {
        var workflow = SetUpWorkflow(ValidDraftJson);

        var result = await CreateHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, "v1"), CancellationToken.None);

        result.VersionNumber.Should().Be(1);
        result.Nodes.Should().HaveCount(2);
        workflow.Status.Should().Be(WorkflowStatus.Published);
        workflow.PublishedVersionNumber.Should().Be(1);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowValidationFailed_WhenTheDraftHasNoStartNode()
    {
        const string invalidDraft = """{"nodes":[{"nodeKey":"end","nodeType":"End","name":"End","description":null,"inputSchemaJson":"{}","outputSchemaJson":"{}","configurationJson":"{}","requiredPermissionsJson":"[]","timeoutSeconds":null,"retryPolicyJson":null,"approvalPolicy":"NeverRequire","idempotencyKeyExpression":null,"compensatingNodeKey":null,"canvasX":0,"canvasY":0}],"connections":[],"variables":[]}""";
        var workflow = SetUpWorkflow(invalidDraft);

        var act = () => CreateHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, null), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WorkflowValidationFailedException>();
        exception.Which.Violations.Should().Contain(v => v.Message.Contains("Start node"));
        workflow.Status.Should().Be(WorkflowStatus.Draft);
    }

    [Fact]
    public async Task Handle_ShouldThrowValidationFailed_WhenTheDraftJsonIsMalformed()
    {
        var workflow = SetUpWorkflow("{not valid json");

        var act = () => CreateHandler().Handle(new PublishWorkflowVersionCommand(workflow.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<WorkflowValidationFailedException>();
    }
}
