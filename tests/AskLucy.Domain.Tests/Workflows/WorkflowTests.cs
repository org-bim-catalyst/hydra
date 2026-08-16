using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Workflows;

public sealed class WorkflowTests
{
    private const string OwnerId = "user-1";

    private static Workflow CreateWorkflow(string name = "My Workflow") =>
        Workflow.Create(OwnerId, name, "desc", WorkflowType.Manual, OwnerId);

    private static WorkflowNodeSpec StartNode() => new(
        "start", WorkflowNodeType.Start, "Start", null, "{}", "{}", "{}", "[]",
        null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

    private static WorkflowNodeSpec EndNode() => new(
        "end", WorkflowNodeType.End, "End", null, "{}", "{}", "{}", "[]",
        null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 100, 0);

    [Fact]
    public void Create_ShouldStartInDraftStatus()
    {
        var workflow = CreateWorkflow();

        workflow.Status.Should().Be(WorkflowStatus.Draft);
        workflow.PublishedVersionNumber.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldThrow_WhenNameIsBlank()
    {
        var act = () => Workflow.Create(OwnerId, "  ", null, WorkflowType.Manual, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldThrow_WhenWorkflowTypeIsScheduled()
    {
        var act = () => Workflow.Create(OwnerId, "My Workflow", null, WorkflowType.Scheduled, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Publish_ShouldCreateVersionOne_AndTransitionToPublished()
    {
        var workflow = CreateWorkflow();

        var version = workflow.Publish([StartNode(), EndNode()], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        version.VersionNumber.Should().Be(1);
        workflow.Status.Should().Be(WorkflowStatus.Published);
        workflow.PublishedVersionNumber.Should().Be(1);
        workflow.Versions.Should().ContainSingle().Which.Should().BeSameAs(version);
        version.Nodes.Should().HaveCount(2);
    }

    [Fact]
    public void Publish_ShouldThrow_WhenNoNodes()
    {
        var workflow = CreateWorkflow();

        var act = () => workflow.Publish([], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Publish_ShouldThrow_WhenNodeKeysAreNotUnique()
    {
        var workflow = CreateWorkflow();

        var act = () => workflow.Publish([StartNode(), StartNode()], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Publish_ShouldResolveConnectionsByNodeKey()
    {
        var workflow = CreateWorkflow();

        var version = workflow.Publish(
            [StartNode(), EndNode()],
            [new WorkflowConnectionSpec("start", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var connection = version.Connections.Should().ContainSingle().Subject;
        connection.SourceNodeId.Should().Be(version.Nodes.Single(n => n.NodeKey == "start").Id);
        connection.TargetNodeId.Should().Be(version.Nodes.Single(n => n.NodeKey == "end").Id);
    }

    [Fact]
    public void Publish_ShouldThrow_WhenCompensatingNodeKeyDoesNotExist()
    {
        var workflow = CreateWorkflow();
        var nodeWithBadCompensation = StartNode() with { CompensatingNodeKey = "does-not-exist" };

        var act = () => workflow.Publish([nodeWithBadCompensation, EndNode()], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Publish_Twice_ShouldProduceASecondImmutableVersion_LeavingTheFirstUnchanged()
    {
        var workflow = CreateWorkflow();
        var version1 = workflow.Publish([StartNode(), EndNode()], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        workflow.UpdateDraft("My Workflow", null, "{\"edited\":true}", OwnerId);
        var version2 = workflow.Publish([StartNode(), EndNode()], [], [], "{}", "{}", "{}", "{}", "{}", "v2", OwnerId);

        version2.VersionNumber.Should().Be(2);
        workflow.PublishedVersionNumber.Should().Be(2);
        workflow.Versions.Should().HaveCount(2);
        version1.Nodes.Should().HaveCount(2);
        version1.Id.Should().NotBe(version2.Id);
    }

    [Fact]
    public void Duplicate_ShouldCopyDraftOnly_NeverVersionOrExecutionHistory()
    {
        var workflow = CreateWorkflow();
        workflow.UpdateDraft("My Workflow", null, "{\"nodes\":[]}", OwnerId);
        workflow.Publish([StartNode(), EndNode()], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        var copy = workflow.Duplicate(OwnerId);

        copy.Id.Should().NotBe(workflow.Id);
        copy.Status.Should().Be(WorkflowStatus.Draft);
        copy.Versions.Should().BeEmpty();
        copy.DraftDefinitionJson.Should().Be("{\"nodes\":[]}");
    }

    [Fact]
    public void Archive_FromDraft_ThenRestore_ShouldReturnToDraft_NotPublished()
    {
        var workflow = CreateWorkflow();

        workflow.Archive(OwnerId);
        workflow.Status.Should().Be(WorkflowStatus.Archived);

        workflow.Restore(OwnerId);

        workflow.Status.Should().Be(WorkflowStatus.Draft);
    }

    [Fact]
    public void Disable_ThenEnable_ShouldRoundTripThroughPublished()
    {
        var workflow = CreateWorkflow();
        workflow.Publish([StartNode(), EndNode()], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        workflow.Disable(OwnerId);
        workflow.Status.Should().Be(WorkflowStatus.Disabled);

        workflow.Enable(OwnerId);
        workflow.Status.Should().Be(WorkflowStatus.Published);
    }

    [Fact]
    public void Disable_ShouldThrow_WhenNotPublished()
    {
        var workflow = CreateWorkflow();

        var act = () => workflow.Disable(OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Deprecate_ShouldTransitionFromPublished()
    {
        var workflow = CreateWorkflow();
        workflow.Publish([StartNode(), EndNode()], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);

        workflow.Deprecate(OwnerId);

        workflow.Status.Should().Be(WorkflowStatus.Deprecated);
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAudit()
    {
        var workflow = CreateWorkflow();

        workflow.SoftDelete(OwnerId);

        workflow.IsDeleted.Should().BeTrue();
        workflow.DeletedBy.Should().Be(OwnerId);
    }
}
