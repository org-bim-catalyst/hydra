using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.RestoreKnowledgeBase;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class RestoreKnowledgeBaseCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private RestoreKnowledgeBaseCommandHandler CreateHandler() => new(_repository, _auditLogRepository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldCancelThePendingPurge_WhenRestoringFromSoftDelete()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.Activate("user-1");
        knowledgeBase.SoftDelete("user-1");
        _repository.GetByIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var result = await CreateHandler().Handle(new RestoreKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        result.IsDeleted.Should().BeFalse();
        knowledgeBase.PurgeScheduledAtUtc.Should().BeNull();
        knowledgeBase.Status.Should().Be(KnowledgeBaseStatus.Active, "restore must preserve the status held before delete");
        _auditLogRepository.Received(1).Add(Arg.Is<KnowledgeBaseAuditLog>(a => a.Action == KnowledgeBaseAuditAction.Restored));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenKnowledgeBaseIsNeitherArchivedNorSoftDeleted()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _repository.GetByIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var act = () => CreateHandler().Handle(new RestoreKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_ForAnotherUsersKnowledgeBase()
    {
        _currentUser.UserId.Returns("attacker");
        var knowledgeBase = KnowledgeBase.Create("KB", "owner-1", "owner-1");
        _repository.GetByIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var act = () => CreateHandler().Handle(new RestoreKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
