using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.DeleteKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.UpdateKnowledgeBaseDetails;
using AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBase;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

/// <summary>Cross-user get/edit/delete on another user's knowledge base return not-found (FR-010) — denial is indistinguishable from a nonexistent id.</summary>
public sealed class KnowledgeBaseOwnershipTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly KnowledgeBaseDashboardSummaryCache _dashboardSummaryCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private KnowledgeBase CreateOtherUsersKnowledgeBase()
    {
        _currentUser.UserId.Returns("attacker");
        var knowledgeBase = KnowledgeBase.Create("Private KB", "owner-1", "owner-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _repository.GetByIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        return knowledgeBase;
    }

    [Fact]
    public async Task Get_ShouldThrowNotFound_ForAnotherUsersKnowledgeBase()
    {
        var knowledgeBase = CreateOtherUsersKnowledgeBase();
        var handler = new GetKnowledgeBaseQueryHandler(_repository, _currentUser);

        var act = () => handler.Handle(new GetKnowledgeBaseQuery(knowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_ShouldThrowNotFound_ForAnotherUsersKnowledgeBase()
    {
        var knowledgeBase = CreateOtherUsersKnowledgeBase();
        var handler = new UpdateKnowledgeBaseDetailsCommandHandler(_repository, _auditLogRepository, _publisher, _unitOfWork, _currentUser);

        var act = () => handler.Handle(
            new UpdateKnowledgeBaseDetailsCommand(knowledgeBase.Id, "Hijacked", null, null, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Delete_ShouldThrowNotFound_ForAnotherUsersKnowledgeBase()
    {
        var knowledgeBase = CreateOtherUsersKnowledgeBase();
        var handler = new DeleteKnowledgeBaseCommandHandler(_repository, _auditLogRepository, _dashboardSummaryCache, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new DeleteKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
