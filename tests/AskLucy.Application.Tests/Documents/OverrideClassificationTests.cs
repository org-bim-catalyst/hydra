using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.OverrideClassification;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T080 — <see cref="OverrideClassificationCommandHandler"/> sets <see cref="DocumentClassificationSource.UserOverride"/> and persists (FR-026).</summary>
public sealed class OverrideClassificationTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private OverrideClassificationCommandHandler CreateSut() => new(_documentRepository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldOverrideAnExistingAutomaticClassification()
    {
        _currentUser.UserId.Returns("user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        var oldCategory = DocumentCategory.Create("Technical", true, "system");
        var newCategory = DocumentCategory.Create("Legal", true, "system");
        var classification = DocumentClassification.CreateAutomatic(document.Id, oldCategory.Id, 0.9m, "system:processing");

        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetCategoryByIdAsync(newCategory.Id, Arg.Any<CancellationToken>()).Returns(newCategory);
        _documentRepository.GetClassificationByDocumentIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(classification);

        var result = await CreateSut().Handle(new OverrideClassificationCommand(document.Id, newCategory.Id), CancellationToken.None);

        result.CategoryId.Should().Be(newCategory.Id);
        result.CategoryName.Should().Be("Legal");
        result.Source.Should().Be(DocumentClassificationSource.UserOverride);
        result.ConfidenceScore.Should().BeNull();
        classification.Source.Should().Be(DocumentClassificationSource.UserOverride);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCreateANewUserOverrideClassification_WhenNoneExistedYet()
    {
        _currentUser.UserId.Returns("user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        var category = DocumentCategory.Create("Legal", true, "system");

        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetCategoryByIdAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _documentRepository.GetClassificationByDocumentIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns((DocumentClassification?)null);

        DocumentClassification? captured = null;
        _documentRepository.When(r => r.AddClassification(Arg.Any<DocumentClassification>())).Do(c => captured = c.Arg<DocumentClassification>());

        var result = await CreateSut().Handle(new OverrideClassificationCommand(document.Id, category.Id), CancellationToken.None);

        result.Source.Should().Be(DocumentClassificationSource.UserOverride);
        captured.Should().NotBeNull();
        captured!.Source.Should().Be(DocumentClassificationSource.UserOverride);
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCategoryDoesNotExist()
    {
        _currentUser.UserId.Returns("user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _documentRepository.GetCategoryByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentCategory?)null);

        var act = () => CreateSut().Handle(new OverrideClassificationCommand(document.Id, Guid.CreateVersion7()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenCallerDoesNotOwnDocument()
    {
        _currentUser.UserId.Returns("user-2");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);

        var act = () => CreateSut().Handle(new OverrideClassificationCommand(document.Id, Guid.CreateVersion7()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
