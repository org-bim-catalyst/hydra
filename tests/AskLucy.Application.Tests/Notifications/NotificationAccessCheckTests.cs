using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Notifications;
using AskLucy.Application.Documents.Notifications;
using AskLucy.Application.KnowledgeBases.Notifications;
using AskLucy.Application.Memory.Notifications;
using AskLucy.Application.Workflows.Notifications;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>T091 — each module's <see cref="AskLucy.Application.Notifications.Abstractions.INotificationAccessCheck"/> is owner-scoped and reuses the same ownership predicate its module's existing guard checks (research R24), without that guard's throw-as-404 behavior.</summary>
public sealed class NotificationAccessCheckTests
{
    private const string OwnerId = "owner-1";
    private const string OtherUserId = "other-user";

    [Fact]
    public async Task DocumentCheck_ShouldReportOwnedDocumentAvailable_AndOthersNot()
    {
        var repository = Substitute.For<IDocumentRepository>();
        var document = Document.Create(Guid.CreateVersion7(), OwnerId, "report.pdf", DocumentFileType.Pdf, 1000, Guid.CreateVersion7(), OwnerId);
        repository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var check = new DocumentNotificationAccessCheck(repository);

        (await check.CanAccessAsync(OwnerId, document.Id.ToString(), CancellationToken.None)).Should().BeTrue();
        (await check.CanAccessAsync(OtherUserId, document.Id.ToString(), CancellationToken.None)).Should().BeFalse();
        (await check.CanAccessAsync(OwnerId, "not-a-guid", CancellationToken.None)).Should().BeFalse();

        var available = await check.GetAvailableAsync(OwnerId, [document.Id.ToString(), Guid.NewGuid().ToString()], CancellationToken.None);
        available.Should().BeEquivalentTo([document.Id.ToString()]);
    }

    [Fact]
    public async Task AgentExecutionCheck_ShouldReportOwnedExecutionAvailable_AndOthersNot()
    {
        var repository = Substitute.For<IAgentExecutionRepository>();
        var execution = AgentExecution.Create(Guid.NewGuid(), Guid.NewGuid(), OwnerId, "Do something.", false, AgentConversationIntegrationMode.Standalone, null, OwnerId);
        repository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        repository.GetByIdForUserAsync(execution.Id, OtherUserId, Arg.Any<CancellationToken>()).Returns((AgentExecution?)null);

        var check = new AgentExecutionNotificationAccessCheck(repository);

        (await check.CanAccessAsync(OwnerId, execution.Id.ToString(), CancellationToken.None)).Should().BeTrue();
        (await check.CanAccessAsync(OtherUserId, execution.Id.ToString(), CancellationToken.None)).Should().BeFalse();

        var available = await check.GetAvailableAsync(OwnerId, [execution.Id.ToString(), Guid.NewGuid().ToString()], CancellationToken.None);
        available.Should().BeEquivalentTo([execution.Id.ToString()]);
    }

    [Fact]
    public async Task WorkflowExecutionCheck_ShouldReportOwnedExecutionAvailable_AndOthersNot()
    {
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        var execution = WorkflowExecution.Create(Guid.NewGuid(), Guid.NewGuid(), OwnerId, WorkflowExecutionTriggerType.Manual, null, "{}", OwnerId);
        repository.GetByIdForUserAsync(execution.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(execution);
        repository.GetByIdForUserAsync(execution.Id, OtherUserId, Arg.Any<CancellationToken>()).Returns((WorkflowExecution?)null);

        var check = new WorkflowExecutionNotificationAccessCheck(repository);

        (await check.CanAccessAsync(OwnerId, execution.Id.ToString(), CancellationToken.None)).Should().BeTrue();
        (await check.CanAccessAsync(OtherUserId, execution.Id.ToString(), CancellationToken.None)).Should().BeFalse();

        var available = await check.GetAvailableAsync(OwnerId, [execution.Id.ToString(), Guid.NewGuid().ToString()], CancellationToken.None);
        available.Should().BeEquivalentTo([execution.Id.ToString()]);
    }

    [Fact]
    public async Task KnowledgeBaseCheck_ShouldReportOwnedKnowledgeBaseAvailable_AndOthersNot()
    {
        var repository = Substitute.For<IKnowledgeBaseRepository>();
        var knowledgeBase = KnowledgeBase.Create("KB", OwnerId, OwnerId);
        repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([knowledgeBase]);

        var check = new KnowledgeBaseNotificationAccessCheck(repository);

        (await check.CanAccessAsync(OwnerId, knowledgeBase.Id.ToString(), CancellationToken.None)).Should().BeTrue();
        (await check.CanAccessAsync(OtherUserId, knowledgeBase.Id.ToString(), CancellationToken.None)).Should().BeFalse();

        var available = await check.GetAvailableAsync(OwnerId, [knowledgeBase.Id.ToString()], CancellationToken.None);
        available.Should().BeEquivalentTo([knowledgeBase.Id.ToString()]);
    }

    [Fact]
    public async Task MemoryCheck_ShouldReportOwnedMemoryAvailable_AndOthersNot()
    {
        var repository = Substitute.For<IMemoryRepository>();
        var memory = MemoryEntity.CreateCandidate(
            OwnerId, null, MemoryCategory.UserPreference, "Prefers dark mode.", MemorySourceType.ExplicitUserStatement,
            null, 0.5m, 0.9m, false, MemoryApprovalMode.Automatic, OwnerId);
        repository.GetByIdAsync(memory.Id, Arg.Any<CancellationToken>()).Returns(memory);

        var check = new MemoryNotificationAccessCheck(repository);

        (await check.CanAccessAsync(OwnerId, memory.Id.ToString(), CancellationToken.None)).Should().BeTrue();
        (await check.CanAccessAsync(OtherUserId, memory.Id.ToString(), CancellationToken.None)).Should().BeFalse();

        var available = await check.GetAvailableAsync(OwnerId, [memory.Id.ToString(), Guid.NewGuid().ToString()], CancellationToken.None);
        available.Should().BeEquivalentTo([memory.Id.ToString()]);
    }
}
