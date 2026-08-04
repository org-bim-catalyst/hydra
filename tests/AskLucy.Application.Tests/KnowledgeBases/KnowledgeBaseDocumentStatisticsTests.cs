using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.DeleteDocument;
using AskLucy.Application.KnowledgeBases.Commands.MoveDocument;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

/// <summary>FR-031: `UploadDocumentCommand`'s own statistics assertions live in `UploadDocumentCommandTests`; this file covers Move (no change expected) and Delete (decrement expected).</summary>
public sealed class KnowledgeBaseDocumentStatisticsTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseFolderRepository _folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
    private readonly IKnowledgeBaseDocumentRepository _documentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly KnowledgeBaseDashboardSummaryCache _dashboardSummaryCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private KnowledgeBase SetUpOwnedKnowledgeBaseWithOneDocument(out KnowledgeBaseDocument document)
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        document = KnowledgeBaseDocument.Create(knowledgeBase.Id, null, "a.pdf", "stored-a", "application/pdf", 500, 4, "user-1");
        knowledgeBase.ApplyDocumentAdded(document.PageCount, document.SizeBytes, "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        return knowledgeBase;
    }

    [Fact]
    public async Task Move_ShouldNotChangeCachedStatistics()
    {
        var knowledgeBase = SetUpOwnedKnowledgeBaseWithOneDocument(out var document);
        var folder = KnowledgeBaseFolder.Create(knowledgeBase.Id, "Folder", null, 0, 10, "user-1");
        _folderRepository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        var handler = new MoveDocumentCommandHandler(_knowledgeBaseRepository, _folderRepository, _documentRepository, _unitOfWork, _currentUser);

        await handler.Handle(new MoveDocumentCommand(knowledgeBase.Id, document.Id, folder.Id), CancellationToken.None);

        document.FolderId.Should().Be(folder.Id);
        knowledgeBase.DocumentCount.Should().Be(1);
        knowledgeBase.TotalPageCount.Should().Be(4);
        knowledgeBase.StorageSizeBytes.Should().Be(500);
    }

    [Fact]
    public async Task Delete_ShouldDecrementCachedStatistics()
    {
        var knowledgeBase = SetUpOwnedKnowledgeBaseWithOneDocument(out var document);
        var handler = new DeleteDocumentCommandHandler(_knowledgeBaseRepository, _documentRepository, _dashboardSummaryCache, _unitOfWork, _currentUser);

        await handler.Handle(new DeleteDocumentCommand(knowledgeBase.Id, document.Id), CancellationToken.None);

        document.IsDeleted.Should().BeTrue();
        knowledgeBase.DocumentCount.Should().Be(0);
        knowledgeBase.TotalPageCount.Should().Be(0);
        knowledgeBase.StorageSizeBytes.Should().Be(0);
    }
}
