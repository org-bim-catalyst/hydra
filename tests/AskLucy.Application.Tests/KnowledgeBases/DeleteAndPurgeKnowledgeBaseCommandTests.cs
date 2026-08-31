using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.DeleteKnowledgeBase;
using AskLucy.Application.KnowledgeBases.Commands.PurgeKnowledgeBase;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class DeleteKnowledgeBaseCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly KnowledgeBaseDashboardSummaryCache _dashboardSummaryCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldSoftDelete_AndScheduleAutomaticPurge()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        var handler = new DeleteKnowledgeBaseCommandHandler(_repository, _auditLogRepository, _dashboardSummaryCache, _unitOfWork, _currentUser);

        await handler.Handle(new DeleteKnowledgeBaseCommand(knowledgeBase.Id), CancellationToken.None);

        knowledgeBase.IsDeleted.Should().BeTrue();
        knowledgeBase.PurgeScheduledAtUtc.Should().NotBeNull();
        _auditLogRepository.Received(1).Add(Arg.Is<KnowledgeBaseAuditLog>(a => a != null && a.Action == KnowledgeBaseAuditAction.Deleted));
    }
}

public sealed class PurgeKnowledgeBaseCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseDocumentRepository _documentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly KnowledgeBaseDashboardSummaryCache _dashboardSummaryCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Microsoft.Extensions.Logging.ILogger<PurgeKnowledgeBaseCommandHandler> _logger =
        Substitute.For<Microsoft.Extensions.Logging.ILogger<PurgeKnowledgeBaseCommandHandler>>();

    private PurgeKnowledgeBaseCommandHandler CreateHandler() =>
        new(_repository, _documentRepository, _auditLogRepository, _dashboardSummaryCache, _fileStorage, _unitOfWork, _currentUser, _logger);

    [Fact]
    public async Task Handle_ShouldThrow_WhenNotConfirmed()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.SoftDelete("user-1");
        _repository.GetByIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        // The confirm=false rejection is enforced by PurgeKnowledgeBaseCommandValidator in the
        // MediatR pipeline, not the handler — verified directly here since this test bypasses
        // that pipeline.
        var validator = new PurgeKnowledgeBaseCommandValidator();
        var result = await validator.ValidateAsync(new PurgeKnowledgeBaseCommand(knowledgeBase.Id, false), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenNotCurrentlySoftDeleted()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _repository.GetByIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var act = () => CreateHandler().Handle(new PurgeKnowledgeBaseCommand(knowledgeBase.Id, true), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldWriteAuditLogBeforeDeletingFiles_ThenCascadeDeleteEveryDocumentFile()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.SoftDelete("user-1");
        _repository.GetByIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var document1 = KnowledgeBaseDocument.Create(knowledgeBase.Id, null, "a.pdf", "stored-a", "application/pdf", 100, 1, "user-1");
        var document2 = KnowledgeBaseDocument.Create(knowledgeBase.Id, null, "b.pdf", "stored-b", "application/pdf", 200, 2, "user-1");
        _documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>())
            .Returns([document1, document2]);

        var callOrder = new List<string>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ => { callOrder.Add("audit-saved"); return 1; });
        _fileStorage.When(f => f.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("file-deleted"));
        _repository.When(r => r.PurgeAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("row-purged"));

        await CreateHandler().Handle(new PurgeKnowledgeBaseCommand(knowledgeBase.Id, true), CancellationToken.None);

        _auditLogRepository.Received(1).Add(Arg.Is<KnowledgeBaseAuditLog>(a => a != null && a.Action == KnowledgeBaseAuditAction.PermanentlyDeleted));
        await _fileStorage.Received(1).DeleteAsync("stored-a", Arg.Any<CancellationToken>());
        await _fileStorage.Received(1).DeleteAsync("stored-b", Arg.Any<CancellationToken>());
        await _repository.Received(1).PurgeAsync(knowledgeBase.Id, Arg.Any<CancellationToken>());
        callOrder.Should().Equal(["audit-saved", "file-deleted", "file-deleted", "row-purged"]);
    }
}
