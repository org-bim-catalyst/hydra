using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.UpdateKnowledgeBaseDetails;
using AskLucy.Application.Workflows.EventTriggers;
using AskLucy.Domain.KnowledgeBases;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

/// <summary>spec.md User Story 9 (research.md Decision 12) — <see cref="KnowledgeBaseUpdatedNotification"/> is published via <see cref="IPublisher"/> only after <c>UpdateKnowledgeBaseDetailsCommandHandler</c>'s own commit has already succeeded.</summary>
public sealed class KnowledgeBaseUpdatedNotificationPublishingTests
{
    [Fact]
    public async Task Handle_ShouldPublishKnowledgeBaseUpdatedNotification_AfterItsCommitSucceeds()
    {
        var repository = Substitute.For<IKnowledgeBaseRepository>();
        var auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
        var publisher = Substitute.For<IPublisher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();

        currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("Old Name", "user-1", "user-1");
        repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var handler = new UpdateKnowledgeBaseDetailsCommandHandler(repository, auditLogRepository, publisher, unitOfWork, currentUser);

        await handler.Handle(new UpdateKnowledgeBaseDetailsCommand(knowledgeBase.Id, "New Name", null, null, null, null, null, null), CancellationToken.None);

        Received.InOrder(() =>
        {
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            publisher.Publish(Arg.Any<KnowledgeBaseUpdatedNotification>(), Arg.Any<CancellationToken>());
        });
        await publisher.Received(1).Publish(
            Arg.Is<KnowledgeBaseUpdatedNotification>(n => n != null && n.KnowledgeBaseId == knowledgeBase.Id && n.OwnerId == "user-1"), Arg.Any<CancellationToken>());
    }
}
