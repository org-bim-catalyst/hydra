using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.Documents.Processing.Stages;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents.Processing;

/// <summary>T059 — one unit test class per <see cref="IProcessingStageHandler"/> implementation, all engines faked.</summary>
public sealed class StageHandlerTests
{
    private static (Document Document, DocumentVersion Version) CreateDocument(DocumentFileType fileType)
    {
        var documentId = Guid.CreateVersion7();
        var version = DocumentVersion.Create(documentId, 1, 0, "stored.bin", "original.bin", 1024, Guid.CreateVersion7(), "user-1");
        var document = Document.Create(documentId, "user-1", "file", fileType, 1024, version.Id, "user-1");
        return (document, version);
    }

    public sealed class ValidationStageHandlerTests
    {
        private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
        private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
        private readonly IDocumentFileValidator _validator = Substitute.For<IDocumentFileValidator>();

        [Fact]
        public async Task ExecuteAsync_ShouldReturnCompleted_WhenValidationSucceeds()
        {
            var (_, version) = CreateDocument(DocumentFileType.Pdf);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream());
            _validator.ValidateAsync(Arg.Any<Stream>(), version.OriginalFileName, Arg.Any<CancellationToken>())
                .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf"));

            var handler = new ValidationStageHandler(_documentRepository, _fileStorage, _validator);
            var outcome = await handler.ExecuteAsync(Guid.CreateVersion7(), version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldThrow_WhenValidationFails()
        {
            var (_, version) = CreateDocument(DocumentFileType.Pdf);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream());
            _validator.ValidateAsync(Arg.Any<Stream>(), version.OriginalFileName, Arg.Any<CancellationToken>())
                .Returns(DocumentFileValidationResult.Invalid("Corrupted file."));

            var handler = new ValidationStageHandler(_documentRepository, _fileStorage, _validator);
            var act = () => handler.ExecuteAsync(Guid.CreateVersion7(), version.Id, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Corrupted file.");
        }
    }

    public sealed class OcrStageHandlerTests
    {
        private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
        private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
        private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        private readonly IDocumentTextExtractor _pdfExtractor = Substitute.For<IDocumentTextExtractor>();
        private readonly IDocumentPreviewGenerator _pdfPreviewGenerator = Substitute.For<IDocumentPreviewGenerator>();
        private readonly IOcrEngine _ocrEngine = Substitute.For<IOcrEngine>();

        private OcrStageHandler CreateSut() => new(
            _documentRepository, _fileStorage, _unitOfWork, [_pdfExtractor], [_pdfPreviewGenerator], _ocrEngine);

        [Fact]
        public async Task ExecuteAsync_ShouldReturnSkipped_ForNonImageNonPdfFormat()
        {
            var (document, version) = CreateDocument(DocumentFileType.Word);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Skipped);
            await _ocrEngine.DidNotReceive().RecognizeAsync(Arg.Any<Stream>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnSkipped_WhenPdfAlreadyHasATextLayer()
        {
            var (document, version) = CreateDocument(DocumentFileType.Pdf);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream());
            _pdfExtractor.CanHandle(DocumentFileType.Pdf).Returns(true);
            _pdfExtractor.ExtractAsync(Arg.Any<Stream>(), DocumentFileType.Pdf, Arg.Any<CancellationToken>())
                .Returns(new DocumentTextExtractionResult("Existing text layer.", null, 1));

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Skipped);
            await _ocrEngine.DidNotReceive().RecognizeAsync(Arg.Any<Stream>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_ShouldOcrRasterizedPages_WhenPdfHasNoTextLayer()
        {
            var (document, version) = CreateDocument(DocumentFileType.Pdf);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream());
            _pdfExtractor.CanHandle(DocumentFileType.Pdf).Returns(true);
            _pdfExtractor.ExtractAsync(Arg.Any<Stream>(), DocumentFileType.Pdf, Arg.Any<CancellationToken>())
                .Returns(new DocumentTextExtractionResult(null, null, 1));
            _pdfPreviewGenerator.CanHandle(DocumentFileType.Pdf).Returns(true);
            _pdfPreviewGenerator.GenerateAsync(Arg.Any<Stream>(), DocumentFileType.Pdf, Arg.Any<CancellationToken>())
                .Returns([new DocumentPreviewResult(DocumentPreviewType.PageImage, [1, 2, 3], 1)]);
            _ocrEngine.RecognizeAsync(Arg.Any<Stream>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(new OcrResult("Recognized scanned text."));

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            version.OcrTextRaw.Should().Be("Recognized scanned text.");
            await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_ShouldOcrTheImageDirectly_ForImageFormats()
        {
            var (document, version) = CreateDocument(DocumentFileType.Png);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream());
            _ocrEngine.RecognizeAsync(Arg.Any<Stream>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(new OcrResult("Recognized image text."));

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            version.OcrTextRaw.Should().Be("Recognized image text.");
        }
    }

    public sealed class TextExtractionStageHandlerTests
    {
        private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
        private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
        private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        private readonly IDocumentTextExtractor _pdfExtractor = Substitute.For<IDocumentTextExtractor>();

        private TextExtractionStageHandler CreateSut() => new(_documentRepository, _fileStorage, _unitOfWork, [_pdfExtractor]);

        [Fact]
        public async Task ExecuteAsync_ShouldUseExtractor_WhenOneCanHandleTheFormat()
        {
            var (document, version) = CreateDocument(DocumentFileType.Pdf);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream());
            _pdfExtractor.CanHandle(DocumentFileType.Pdf).Returns(true);
            _pdfExtractor.ExtractAsync(Arg.Any<Stream>(), DocumentFileType.Pdf, Arg.Any<CancellationToken>())
                .Returns(new DocumentTextExtractionResult("Body text.", "{\"headings\":[]}", 3));

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            version.ExtractedText.Should().Be("Body text.");
            version.PageCount.Should().Be(3);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReadRawContent_ForPlainTextFormats()
        {
            var (document, version) = CreateDocument(DocumentFileType.Markdown);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>())
                .Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("# Heading\ncontent")));
            _pdfExtractor.CanHandle(DocumentFileType.Markdown).Returns(false);

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            version.ExtractedText.Should().Be("# Heading\ncontent");
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnSkipped_ForImageFormats()
        {
            var (document, version) = CreateDocument(DocumentFileType.Png);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _pdfExtractor.CanHandle(DocumentFileType.Png).Returns(false);

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Skipped);
            await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    public sealed class MetadataExtractionStageHandlerTests
    {
        private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
        private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
        private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        private readonly IDocumentTextExtractor _pdfExtractor = Substitute.For<IDocumentTextExtractor>();

        private MetadataExtractionStageHandler CreateSut() => new(_documentRepository, _fileStorage, _unitOfWork, [_pdfExtractor]);

        [Fact]
        public async Task ExecuteAsync_ShouldPersistCoreProperties_WhenAnExtractorIsAvailable()
        {
            var (document, version) = CreateDocument(DocumentFileType.Pdf);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream());
            _pdfExtractor.CanHandle(DocumentFileType.Pdf).Returns(true);
            _pdfExtractor.ExtractAsync(Arg.Any<Stream>(), DocumentFileType.Pdf, Arg.Any<CancellationToken>())
                .Returns(new DocumentTextExtractionResult(null, null, null, Title: "Report", Author: "Jane Doe", Keywords: "bim, catalyst"));

            DocumentMetadata? captured = null;
            _documentRepository.When(r => r.AddMetadata(Arg.Any<DocumentMetadata>())).Do(c => captured = c.Arg<DocumentMetadata>());

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            captured.Should().NotBeNull();
            captured!.Title.Should().Be("Report");
            captured.Author.Should().Be("Jane Doe");
            captured.Keywords.Should().Be("bim, catalyst");
        }

        [Fact]
        public async Task ExecuteAsync_ShouldPersistEncodingOnly_WhenNoExtractorIsAvailable()
        {
            var (document, version) = CreateDocument(DocumentFileType.Text);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _pdfExtractor.CanHandle(DocumentFileType.Text).Returns(false);

            DocumentMetadata? captured = null;
            _documentRepository.When(r => r.AddMetadata(Arg.Any<DocumentMetadata>())).Do(c => captured = c.Arg<DocumentMetadata>());

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            captured!.Encoding.Should().Be("UTF-8");
            captured.Title.Should().BeNull();
        }
    }

    public sealed class ClassificationStageHandlerTests
    {
        private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
        private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        private readonly IDocumentLanguageAndClassifier _classifier = Substitute.For<IDocumentLanguageAndClassifier>();

        private ClassificationStageHandler CreateSut() => new(_documentRepository, _unitOfWork, _classifier);

        [Fact]
        public async Task ExecuteAsync_ShouldReturnSkipped_WhenNoTextWasExtracted()
        {
            var (document, version) = CreateDocument(DocumentFileType.Png);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Skipped);
            await _classifier.DidNotReceive().AnalyzeAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_ShouldPersistClassificationAndLanguages_WhenTextIsAvailable()
        {
            var (document, version) = CreateDocument(DocumentFileType.Pdf);
            version.ApplyExtractedText("Some legal text.", null, 1, "system");
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

            var legalCategory = DocumentCategory.Create("Legal", true, "system");
            _documentRepository.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns([legalCategory]);
            _classifier.AnalyzeAsync("Some legal text.", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(new DocumentLanguageAndClassificationResult(
                    [new DetectedLanguage("en", DocumentLanguageRole.Primary, 0.95m)],
                    new DocumentClassificationResult("Legal", 0.9m)));

            DocumentClassification? capturedClassification = null;
            _documentRepository.When(r => r.AddClassification(Arg.Any<DocumentClassification>()))
                .Do(c => capturedClassification = c.Arg<DocumentClassification>());
            DocumentLanguage? capturedLanguage = null;
            _documentRepository.When(r => r.AddLanguage(Arg.Any<DocumentLanguage>()))
                .Do(c => capturedLanguage = c.Arg<DocumentLanguage>());

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            capturedClassification!.CategoryId.Should().Be(legalCategory.Id);
            capturedClassification.ConfidenceScore.Should().Be(0.9m);
            capturedLanguage!.LanguageCode.Should().Be("en");
        }
    }

    public sealed class LanguageDetectionStageHandlerTests
    {
        [Fact]
        public async Task ExecuteAsync_ShouldAlwaysReturnSkipped()
        {
            var outcome = await new LanguageDetectionStageHandler().ExecuteAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Skipped);
        }
    }

    public sealed class PreviewGenerationStageHandlerTests
    {
        private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
        private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
        private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        private readonly IDocumentPreviewGenerator _pdfGenerator = Substitute.For<IDocumentPreviewGenerator>();

        private PreviewGenerationStageHandler CreateSut() => new(_documentRepository, _fileStorage, _unitOfWork, [_pdfGenerator]);

        [Fact]
        public async Task ExecuteAsync_ShouldPersistStructuredContentPreview_ForOfficeFormats()
        {
            var (document, version) = CreateDocument(DocumentFileType.Word);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

            DocumentPreview? captured = null;
            _documentRepository.When(r => r.AddPreview(Arg.Any<DocumentPreview>())).Do(c => captured = c.Arg<DocumentPreview>());

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            captured!.PreviewType.Should().Be(DocumentPreviewType.StructuredContent);
            captured.StoredFileName.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnSkipped_WhenFormatHasNoPreviewSupport()
        {
            var (document, version) = CreateDocument(DocumentFileType.Rtf);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _pdfGenerator.CanHandle(DocumentFileType.Rtf).Returns(false);

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Skipped);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldRenderAndPersistPageImages_ForPdf()
        {
            var (document, version) = CreateDocument(DocumentFileType.Pdf);
            _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
            _documentRepository.GetVersionByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            _fileStorage.OpenReadAsync(version.StoredFileName, Arg.Any<CancellationToken>()).Returns(new MemoryStream());
            _pdfGenerator.CanHandle(DocumentFileType.Pdf).Returns(true);
            _pdfGenerator.GenerateAsync(Arg.Any<Stream>(), DocumentFileType.Pdf, Arg.Any<CancellationToken>())
                .Returns([
                    new DocumentPreviewResult(DocumentPreviewType.PageImage, [1, 2, 3], 1),
                    new DocumentPreviewResult(DocumentPreviewType.Thumbnail, [4, 5, 6], null),
                ]);
            _fileStorage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("preview-stored.png");

            var previews = new List<DocumentPreview>();
            _documentRepository.When(r => r.AddPreview(Arg.Any<DocumentPreview>())).Do(c => previews.Add(c.Arg<DocumentPreview>()!));

            var outcome = await CreateSut().ExecuteAsync(document.Id, version.Id, CancellationToken.None);

            outcome.Should().Be(ProcessingStageOutcome.Completed);
            previews.Should().HaveCount(2);
            previews.Should().OnlyContain(p => p.StoredFileName == "preview-stored.png");
        }
    }
}
