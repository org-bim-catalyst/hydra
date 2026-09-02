using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.ActivateKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.ArchiveKnowledgeBase;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

/// <summary>Activate/Archive state guards and idempotency (US3). Restore is covered by `RestoreKnowledgeBaseCommandTests.cs` (US1, pulled forward — see tasks.md T036's note) — including the "restore preserves favorite/pinned state" edge case, since neither `Archive` nor `SoftDelete` ever touch `IsFavorite`/`PinnedAtUtc`.</summary>
public sealed class KnowledgeBaseLifecycleTransitionTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Activate_ShouldTransitionDraftToActive()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var handler = new ActivateKnowledgeBaseCommandHandler(_repository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new ActivateKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        result.Status.Should().Be(KnowledgeBaseStatus.Active);
    }

    [Fact]
    public async Task Activate_ShouldThrow_WhenNotCurrentlyDraft()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.Activate("user-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var handler = new ActivateKnowledgeBaseCommandHandler(_repository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new ActivateKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Archive_ShouldTransitionActiveToArchived_AndWriteAuditLog()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.Activate("user-1");
        knowledgeBase.MarkFavorite("user-1");
        knowledgeBase.Pin("user-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var handler = new ArchiveKnowledgeBaseCommandHandler(_repository, _auditLogRepository, _unitOfWork, _currentUser);

        var result = await handler.Handle(new ArchiveKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        result.Status.Should().Be(KnowledgeBaseStatus.Archived);
        result.IsFavorite.Should().BeTrue("archiving a favorited knowledge base must keep it favorited (spec.md Edge Cases)");
        result.IsPinned.Should().BeTrue();
        _auditLogRepository.Received(1).Add(Arg.Is<KnowledgeBaseAuditLog>(a => a != null && a.Action == KnowledgeBaseAuditAction.Archived));
    }

    [Fact]
    public async Task Archive_ShouldThrow_WhenNotCurrentlyActive()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var handler = new ArchiveKnowledgeBaseCommandHandler(_repository, _auditLogRepository, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new ArchiveKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
