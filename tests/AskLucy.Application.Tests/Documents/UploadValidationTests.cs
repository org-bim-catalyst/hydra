using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.Documents.Commands.CompleteUpload;
using AskLucy.Application.Documents.Commands.StartUpload;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Documents;

public sealed class UploadValidationTests
{
    private readonly IDocumentUploadSessionRepository _sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
    private readonly IResumableUploadStorage _resumableStorage = Substitute.For<IResumableUploadStorage>();
    private readonly IDocumentFileValidator _fileValidator = Substitute.For<IDocumentFileValidator>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IDocumentStatisticsRepository _statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
    private readonly IProcessingNotifier _processingNotifier = Substitute.For<IProcessingNotifier>();
    private readonly IDocumentProcessingPipeline _processingPipeline = Substitute.For<IDocumentProcessingPipeline>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public UploadValidationTests()
    {
        _statisticsRepository.ComputeAggregateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(0, 0, null, "{}", "{}"));
    }

    private static IOptions<DocumentUploadOptions> Options(long maxFileSizeBytes = 10_000) =>
        Microsoft.Extensions.Options.Options.Create(new DocumentUploadOptions { MaxFileSizeBytes = maxFileSizeBytes });

    private static IOptions<DocumentStorageQuotaOptions> QuotaOptions() =>
        Microsoft.Extensions.Options.Options.Create(new DocumentStorageQuotaOptions());

    [Fact]
    public async Task StartUpload_ShouldReject_BeforeAnyChunkIsAccepted_WhenDeclaredSizeExceedsMax()
    {
        _currentUser.UserId.Returns("user-1");
        var handler = new StartUploadCommandHandler(
            _sessionRepository, _documentRepository, _statisticsRepository, _processingNotifier,
            Options(maxFileSizeBytes: 100), QuotaOptions(), _unitOfWork, _currentUser);

        var act = () => handler.Handle(new StartUploadCommand("huge.pdf", 500), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        _sessionRepository.DidNotReceive().Add(Arg.Any<DocumentUploadSession>());
    }

    [Fact]
    public async Task CompleteUpload_ShouldReject_WhenContentDoesNotMatchExtension()
    {
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "fake.pdf", 10, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(10L);
        _resumableStorage.OpenReadAsync(session.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("plain text, not a pdf"))));
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), "fake.pdf", Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Invalid("File content looks like plain text, but its extension 'pdf' is not supported."));

        var finalizer = new DocumentUploadFinalizer(_fileValidator, _fileStorage, _documentRepository, _statisticsRepository, _processingNotifier, Options(), QuotaOptions());
        var handler = new CompleteUploadCommandHandler(
            _sessionRepository, _resumableStorage, finalizer, _processingPipeline, _processingNotifier, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new CompleteUploadCommand(session.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        _ = _fileStorage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CompleteUpload_ShouldReject_WhenAccumulatedBytesDoNotMatchDeclaredSize()
    {
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 1000, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(500L); // incomplete

        var finalizer = new DocumentUploadFinalizer(_fileValidator, _fileStorage, _documentRepository, _statisticsRepository, _processingNotifier, Options(), QuotaOptions());
        var handler = new CompleteUploadCommandHandler(
            _sessionRepository, _resumableStorage, finalizer, _processingPipeline, _processingNotifier, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new CompleteUploadCommand(session.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>()
            .WithMessage("*incomplete*");
    }
}
