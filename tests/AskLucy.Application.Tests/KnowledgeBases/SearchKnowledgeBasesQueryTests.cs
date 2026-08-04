using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Queries.SearchKnowledgeBases;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

/// <summary>Handler-level parameter pass-through/mapping (NSubstitute-based, per this project's Application.Tests convention) — real filter/sort/cursor behavior against SQL Server is covered by `KnowledgeBaseCursorPaginationTests.cs` (Persistence.Tests), mirroring `SearchUserChatsQueryHandlerTests.cs`'s documented split.</summary>
public sealed class SearchKnowledgeBasesQueryHandlerTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldPassEveryParameterThrough_ToTheRepository()
    {
        _currentUser.UserId.Returns("user-1");
        _repository.SearchAsync(
                "user-1", KnowledgeBaseListView.Archived, "bim", Arg.Any<Guid?>(), "revit", true, true,
                KnowledgeBaseSort.Name, false, "cursor-token", 25, Arg.Any<CancellationToken>())
            .Returns((new List<KnowledgeBase>(), (string?)null));
        var categoryId = Guid.NewGuid();
        var handler = new SearchKnowledgeBasesQueryHandler(_repository, _currentUser);

        await handler.Handle(
            new SearchKnowledgeBasesQuery(
                KnowledgeBaseListView.Archived, "bim", categoryId, "revit", true, true, KnowledgeBaseSort.Name, false, "cursor-token", 25),
            CancellationToken.None);

        await _repository.Received(1).SearchAsync(
            "user-1", KnowledgeBaseListView.Archived, "bim", categoryId, "revit", true, true,
            KnowledgeBaseSort.Name, false, "cursor-token", 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldMapEntitiesToSummaryDtos_AndForwardTheCursor()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        _repository.SearchAsync(
                Arg.Any<string>(), Arg.Any<KnowledgeBaseListView>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool?>(), Arg.Any<KnowledgeBaseSort>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((new List<KnowledgeBase> { knowledgeBase }, "next-cursor"));
        var handler = new SearchKnowledgeBasesQueryHandler(_repository, _currentUser);

        var result = await handler.Handle(new SearchKnowledgeBasesQuery(), CancellationToken.None);

        result.Items.Should().ContainSingle(dto => dto.Id == knowledgeBase.Id);
        result.NextCursor.Should().Be("next-cursor");
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);
        var handler = new SearchKnowledgeBasesQueryHandler(_repository, _currentUser);

        var act = () => handler.Handle(new SearchKnowledgeBasesQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
