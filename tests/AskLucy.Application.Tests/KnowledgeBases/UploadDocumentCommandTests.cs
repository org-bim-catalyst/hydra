using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.UploadDocument;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class UploadDocumentCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseFolderRepository _folderRepository = Substitute.For<IKnowledgeBaseFolderRepository>();
    private readonly IKnowledgeBaseDocumentRepository _documentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly IDocumentContentValidator _contentValidator = Substitute.For<IDocumentContentValidator>();
    private readonly IDocumentPageCountExtractor _pageCountExtractor = Substitute.For<IDocumentPageCountExtractor>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly KnowledgeBaseDashboardSummaryCache _dashboardSummaryCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private UploadDocumentCommandHandler CreateHandler(long maxFileSizeBytes = 50 * 1024 * 1024) => new(
        _knowledgeBaseRepository, _folderRepository, _documentRepository, _contentValidator, _pageCountExtractor, _fileStorage,
        Microsoft.Extensions.Options.Options.Create(new KnowledgeBaseDocumentOptions { MaxFileSizeBytes = maxFileSizeBytes }),
        _dashboardSummaryCache, _unitOfWork, _currentUser);

    private KnowledgeBase SetUpOwnedKnowledgeBase()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        return knowledgeBase;
    }

    [Fact]
    public async Task Handle_ShouldRejectAContentTypeMismatch_WithASpecificMessage()
    {
        var knowledgeBase = SetUpOwnedKnowledgeBase();
        _contentValidator.ValidateAsync(Arg.Any<Stream>(), "fake.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentValidationResult.Invalid("File content is plain text, but its name has extension '.pdf'."));

        var act = () => CreateHandler().Handle(
            new UploadDocumentCommand(knowledgeBase.Id, null, new MemoryStream(), "fake.pdf", 100), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Contain("plain text");
        await _fileStorage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectAnOversizedFile()
    {
        var knowledgeBase = SetUpOwnedKnowledgeBase();

        var act = () => CreateHandler(maxFileSizeBytes: 100).Handle(
            new UploadDocumentCommand(knowledgeBase.Id, null, new MemoryStream(), "big.pdf", 200), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await _contentValidator.DidNotReceive().ValidateAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldExtractPageCount_ForAPdf()
    {
        var knowledgeBase = SetUpOwnedKnowledgeBase();
        _contentValidator.ValidateAsync(Arg.Any<Stream>(), "doc.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Pdf, "application/pdf"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), "doc.pdf", Arg.Any<CancellationToken>()).Returns("stored-doc.pdf");
        _pageCountExtractor.ExtractPageCountAsync(Arg.Any<Stream>(), KnowledgeBaseDocumentType.Pdf, Arg.Any<CancellationToken>()).Returns(12);

        var result = await CreateHandler().Handle(
            new UploadDocumentCommand(knowledgeBase.Id, null, new MemoryStream(), "doc.pdf", 100), CancellationToken.None);

        result.PageCount.Should().Be(12);
        result.ProcessingStatus.Should().Be(KnowledgeBaseDocumentProcessingStatus.Ready);
        knowledgeBase.TotalPageCount.Should().Be(12);
        knowledgeBase.DocumentCount.Should().Be(1);
        _documentRepository.Received(1).Add(Arg.Any<KnowledgeBaseDocument>());
    }

    [Fact]
    public async Task Handle_ShouldMarkProcessingFailed_WhenPageCountExtractionFailsForAPaginatedType()
    {
        var knowledgeBase = SetUpOwnedKnowledgeBase();
        _contentValidator.ValidateAsync(Arg.Any<Stream>(), "corrupt.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Pdf, "application/pdf"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), "corrupt.pdf", Arg.Any<CancellationToken>()).Returns("stored-corrupt.pdf");
        _pageCountExtractor.ExtractPageCountAsync(Arg.Any<Stream>(), KnowledgeBaseDocumentType.Pdf, Arg.Any<CancellationToken>())
            .Returns((int?)null);

        var result = await CreateHandler().Handle(
            new UploadDocumentCommand(knowledgeBase.Id, null, new MemoryStream(), "corrupt.pdf", 100), CancellationToken.None);

        result.PageCount.Should().BeNull();
        result.ProcessingStatus.Should().Be(KnowledgeBaseDocumentProcessingStatus.Failed, "a null page count on a PDF is a parse failure, not N/A");
    }

    [Fact]
    public async Task Handle_ShouldStayReady_WhenPageCountIsNullForANonPaginatedType()
    {
        var knowledgeBase = SetUpOwnedKnowledgeBase();
        _contentValidator.ValidateAsync(Arg.Any<Stream>(), "notes.csv", Arg.Any<CancellationToken>())
            .Returns(DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Csv, "text/csv"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), "notes.csv", Arg.Any<CancellationToken>()).Returns("stored-notes.csv");
        _pageCountExtractor.ExtractPageCountAsync(Arg.Any<Stream>(), KnowledgeBaseDocumentType.Csv, Arg.Any<CancellationToken>())
            .Returns((int?)null);

        var result = await CreateHandler().Handle(
            new UploadDocumentCommand(knowledgeBase.Id, null, new MemoryStream(), "notes.csv", 100), CancellationToken.None);

        result.PageCount.Should().BeNull();
        result.ProcessingStatus.Should().Be(KnowledgeBaseDocumentProcessingStatus.Ready, "CSV has no meaningful page count — null here is N/A, not a failure");
    }
}
