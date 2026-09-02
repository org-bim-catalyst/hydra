using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents;
using AskLucy.Application.Documents.Queries.SearchDocuments;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;

namespace AskLucy.Application.Tests.Documents;

/// <summary>
/// T093 — <c>SearchDocuments</c> with combined filters (FR-035–FR-037). The actual filter
/// intersection logic is a real EF Core LINQ-to-SQL translation living in
/// <c>DocumentRepository.SearchAsync</c> (subqueries against DocumentMetadata/DocumentLanguage/
/// DocumentClassification/Tags) — not meaningfully fakeable here, so this verifies the handler
/// correctly forwards every filter through to the repository unchanged; the real intersection
/// behavior is proven against SQL Server in
/// <c>tests/AskLucy.Persistence.Tests/Documents/DocumentSearchFilterTests.cs</c>.
/// </summary>
public sealed class SearchDocumentsFilterTests
{
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldForwardEveryFilterToTheRepository_Unchanged()
    {
        _currentUser.UserId.Returns("user-1");
        var categoryId = Guid.CreateVersion7();
        var dateFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dateTo = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        _documentRepository.SearchAsync(
                "user-1", DocumentListView.Active, null, Arg.Any<DocumentSearchFilters>(), null, 50, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Document>)[], (string?)null));

        var handler = new SearchDocumentsQueryHandler(_documentRepository, _currentUser);
        var query = new SearchDocumentsQuery(
            DocumentListView.Active, null, null, 50,
            Query: "invoice", Author: "Jane", LanguageCode: "en", Tag: "Reviewed",
            CategoryId: categoryId, DateFrom: dateFrom, DateTo: dateTo, Status: DocumentProcessingStatus.Completed);

        await handler.Handle(query, CancellationToken.None);

        await _documentRepository.Received(1).SearchAsync(
            "user-1", DocumentListView.Active, null,
            Arg.Is<DocumentSearchFilters>(f =>
                f != null
                && f.Query == "invoice" && f.Author == "Jane" && f.LanguageCode == "en" && f.Tag == "Reviewed" &&
                f.CategoryId == categoryId && f.DateFrom == dateFrom && f.DateTo == dateTo && f.Status == DocumentProcessingStatus.Completed),
            null, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassNoneFilters_WhenNoneAreSupplied()
    {
        _currentUser.UserId.Returns("user-1");
        _documentRepository.SearchAsync(
                Arg.Any<string>(), Arg.Any<DocumentListView>(), Arg.Any<Guid?>(), Arg.Any<DocumentSearchFilters>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Document>)[], (string?)null));

        var handler = new SearchDocumentsQueryHandler(_documentRepository, _currentUser);
        await handler.Handle(new SearchDocumentsQuery(DocumentListView.Active, null, null, 50), CancellationToken.None);

        await _documentRepository.Received(1).SearchAsync(
            "user-1", DocumentListView.Active, null,
            Arg.Is<DocumentSearchFilters>(f => f == DocumentSearchFilters.None),
            null, 50, Arg.Any<CancellationToken>());
    }
}
