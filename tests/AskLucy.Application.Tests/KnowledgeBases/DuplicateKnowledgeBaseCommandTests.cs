using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.DuplicateKnowledgeBase;
using AskLucy.Application.Options;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

/// <summary>Covers the independent-physical-file-copy guarantee (research.md Decision 4) — every document's file is re-opened and re-saved through <see cref="IFileStorage"/> rather than the copy referencing the source's `StoredFileName`.</summary>
public sealed class DuplicateKnowledgeBaseCommandTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseFolderRepository _folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
    private readonly IKnowledgeBaseDocumentRepository _documentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly KnowledgeBaseDashboardSummaryCache _dashboardSummaryCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private DuplicateKnowledgeBaseCommandHandler CreateHandler() => new(
        _knowledgeBaseRepository, _folderRepository, _documentRepository, _auditLogRepository, _fileStorage,
        Microsoft.Extensions.Options.Options.Create(new KnowledgeBaseFolderOptions()), _dashboardSummaryCache, _unitOfWork, _currentUser);

    private KnowledgeBase SetUpOwnedKnowledgeBase()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("BIM Standards", "user-1", "user-1");
        knowledgeBase.AddTag("revit", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _folderRepository.ListByKnowledgeBaseIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([]);
        _documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns([]);
        return knowledgeBase;
    }

    [Fact]
    public async Task Handle_ShouldCreateADraftCopy_NamedCopyOf_WithTheSourcesDetailsAndTags()
    {
        var source = SetUpOwnedKnowledgeBase();

        var result = await CreateHandler().Handle(new DuplicateKnowledgeBaseCommand(source.Id), CancellationToken.None);

        result.Name.Should().Be("Copy of BIM Standards");
        result.Status.Should().Be(KnowledgeBaseStatus.Draft);
        result.Tags.Should().ContainSingle().Which.Should().Be("revit");
        result.Id.Should().NotBe(source.Id);
        _knowledgeBaseRepository.Received(1).Add(Arg.Is<KnowledgeBase>(k => k.Name == "Copy of BIM Standards"));
    }

    [Fact]
    public async Task Handle_ShouldRecordAnAuditLogEntry_OnTheSource()
    {
        var source = SetUpOwnedKnowledgeBase();

        await CreateHandler().Handle(new DuplicateKnowledgeBaseCommand(source.Id), CancellationToken.None);

        _auditLogRepository.Received(1).Add(Arg.Is<KnowledgeBaseAuditLog>(
            a => a.KnowledgeBaseId == source.Id && a.Action == KnowledgeBaseAuditAction.Duplicated));
    }

    [Fact]
    public async Task Handle_ShouldReCreateEveryDocument_ByOpeningTheSourceFileAndSavingANewIndependentCopy()
    {
        var source = SetUpOwnedKnowledgeBase();
        var sourceDocument = KnowledgeBaseDocument.Create(source.Id, null, "standards.pdf", "stored-standards.pdf", "application/pdf", 2048, 10, "user-1");
        source.ApplyDocumentAdded(10, 2048, "user-1");
        _documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(source.Id, Arg.Any<CancellationToken>()).Returns([sourceDocument]);
        var sourceStream = new MemoryStream();
        _fileStorage.OpenReadAsync("stored-standards.pdf", Arg.Any<CancellationToken>()).Returns(sourceStream);
        _fileStorage.SaveAsync(sourceStream, "standards.pdf", Arg.Any<CancellationToken>()).Returns("stored-standards-copy.pdf");

        var result = await CreateHandler().Handle(new DuplicateKnowledgeBaseCommand(source.Id), CancellationToken.None);

        result.DocumentCount.Should().Be(1);
        _documentRepository.Received(1).Add(Arg.Is<KnowledgeBaseDocument>(d =>
            d.StoredFileName == "stored-standards-copy.pdf" && d.FileName == "standards.pdf" && d.KnowledgeBaseId == result.Id));
        await _fileStorage.Received(1).OpenReadAsync("stored-standards.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotCopyASoftDeletedDocument()
    {
        var source = SetUpOwnedKnowledgeBase();
        var deletedDocument = KnowledgeBaseDocument.Create(source.Id, null, "old.pdf", "stored-old.pdf", "application/pdf", 100, 1, "user-1");
        deletedDocument.SoftDelete("user-1");
        _documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(source.Id, Arg.Any<CancellationToken>()).Returns([deletedDocument]);

        var result = await CreateHandler().Handle(new DuplicateKnowledgeBaseCommand(source.Id), CancellationToken.None);

        result.DocumentCount.Should().Be(0);
        _documentRepository.DidNotReceive().Add(Arg.Any<KnowledgeBaseDocument>());
        await _fileStorage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldDeepCopyTheFolderTree_PreservingHierarchy()
    {
        var source = SetUpOwnedKnowledgeBase();
        var root = KnowledgeBaseFolder.Create(source.Id, "Root", null, 0, 10, "user-1");
        var child = KnowledgeBaseFolder.Create(source.Id, "Child", root.Id, root.Depth, 10, "user-1");
        _folderRepository.ListByKnowledgeBaseIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns([root, child]);
        KnowledgeBaseFolder? capturedRootCopy = null;
        KnowledgeBaseFolder? capturedChildCopy = null;
        _folderRepository.When(r => r.Add(Arg.Is<KnowledgeBaseFolder>(f => f.Name == "Root"))).Do(ci => capturedRootCopy = ci.Arg<KnowledgeBaseFolder>());
        _folderRepository.When(r => r.Add(Arg.Is<KnowledgeBaseFolder>(f => f.Name == "Child"))).Do(ci => capturedChildCopy = ci.Arg<KnowledgeBaseFolder>());

        await CreateHandler().Handle(new DuplicateKnowledgeBaseCommand(source.Id), CancellationToken.None);

        capturedRootCopy.Should().NotBeNull();
        capturedChildCopy.Should().NotBeNull();
        capturedRootCopy!.ParentFolderId.Should().BeNull();
        capturedChildCopy!.ParentFolderId.Should().Be(capturedRootCopy.Id, "the copied child must point at the copied parent, not the source parent");
        capturedChildCopy.Id.Should().NotBe(child.Id);
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenTheSourceIsNotOwnedByTheCaller()
    {
        _currentUser.UserId.Returns("user-1");
        var othersKnowledgeBase = KnowledgeBase.Create("Theirs", "user-2", "user-2");
        _knowledgeBaseRepository.GetByIdAsync(othersKnowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(othersKnowledgeBase);

        var act = () => CreateHandler().Handle(new DuplicateKnowledgeBaseCommand(othersKnowledgeBase.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
