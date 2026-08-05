using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.AddTag;
using AskLucy.Application.Documents.Commands.RemoveTag;
using AskLucy.Application.Documents.Queries.ListTags;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>T081 — <c>AddTag</c>/<c>RemoveTag</c>, tag reuse per owner, tags usable as a search filter surface (FR-032).</summary>
public sealed class DocumentTagTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private Document CreateOwnedDocument()
    {
        _currentUser.UserId.Returns("user-1");
        var document = Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        _documentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        return document;
    }

    [Fact]
    public async Task AddTag_ShouldCreateANewTag_WhenNoneWithThatNameExistsForTheOwner()
    {
        var document = CreateOwnedDocument();
        _documentRepository.FindTagByOwnerAndNameAsync("user-1", "Invoices", Arg.Any<CancellationToken>()).Returns((DocumentTag?)null);
        DocumentTag? created = null;
        _documentRepository.When(r => r.AddTag(Arg.Any<DocumentTag>())).Do(c => created = c.Arg<DocumentTag>());

        var handler = new AddTagCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        var result = await handler.Handle(new AddTagCommand(document.Id, "Invoices"), CancellationToken.None);

        created.Should().NotBeNull();
        created!.Name.Should().Be("Invoices");
        result.Should().Contain("Invoices");
        document.Tags.Should().ContainSingle(t => t.Name == "Invoices");
    }

    [Fact]
    public async Task AddTag_ShouldReuseTheExistingTag_WhenOneWithThatNameAlreadyExistsForTheOwner()
    {
        var document = CreateOwnedDocument();
        var existingTag = DocumentTag.Create("user-1", "Invoices", "user-1");
        _documentRepository.FindTagByOwnerAndNameAsync("user-1", "Invoices", Arg.Any<CancellationToken>()).Returns(existingTag);

        var handler = new AddTagCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        await handler.Handle(new AddTagCommand(document.Id, "Invoices"), CancellationToken.None);

        _documentRepository.DidNotReceive().AddTag(Arg.Any<DocumentTag>());
        document.Tags.Should().ContainSingle(t => t.Id == existingTag.Id);
    }

    [Fact]
    public async Task RemoveTag_ShouldDetachTheTag_ButNeverDeleteTheSharedTagRow()
    {
        var document = CreateOwnedDocument();
        var tag = DocumentTag.Create("user-1", "Invoices", "user-1");
        document.AddTag(tag, "user-1");
        _documentRepository.FindTagByOwnerAndNameAsync("user-1", "Invoices", Arg.Any<CancellationToken>()).Returns(tag);

        var handler = new RemoveTagCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        await handler.Handle(new RemoveTagCommand(document.Id, "Invoices"), CancellationToken.None);

        document.Tags.Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveTag_ShouldBeANoOp_WhenTheTagDoesNotExist()
    {
        var document = CreateOwnedDocument();
        _documentRepository.FindTagByOwnerAndNameAsync("user-1", "Nonexistent", Arg.Any<CancellationToken>()).Returns((DocumentTag?)null);

        var handler = new RemoveTagCommandHandler(_documentRepository, _unitOfWork, _currentUser);
        await handler.Handle(new RemoveTagCommand(document.Id, "Nonexistent"), CancellationToken.None);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListTags_ShouldReturnOnlyTheCallersOwnTagNames()
    {
        _currentUser.UserId.Returns("user-1");
        var tags = new List<DocumentTag> { DocumentTag.Create("user-1", "Invoices", "user-1"), DocumentTag.Create("user-1", "Contracts", "user-1") };
        _documentRepository.ListTagsByOwnerAsync("user-1", Arg.Any<CancellationToken>()).Returns(tags);

        var handler = new ListTagsQueryHandler(_documentRepository, _currentUser);
        var result = await handler.Handle(new ListTagsQuery(), CancellationToken.None);

        result.Should().BeEquivalentTo(["Invoices", "Contracts"]);
    }
}
