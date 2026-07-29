using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>
/// Covers FR-019–FR-024 parameter pass-through/mapping and FR-026 ownership scoping.
/// Real full-text/cursor-pagination behavior against SQL Server is covered by
/// tests/AskLucy.Persistence.Tests/Chats/UserChatFullTextSearchTests.cs and
/// CursorPaginationTests.cs (this handler only orchestrates, per constitution §6).
/// </summary>
public sealed class SearchUserChatsQueryHandlerTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldPassParametersThrough_AndMapResultsToSummaryDtos()
    {
        _currentUser.UserId.Returns("owner-1");
        var chat = UserChat.Create("My chat", "owner-1", null, "owner-1");
        chat.Pin("owner-1");
        _repository.SearchAsync(
                "owner-1", ConversationView.Archived, true, true, "budget", ConversationSort.Alphabetical, "cursor-1", 25,
                Arg.Any<CancellationToken>())
            .Returns(([chat], "cursor-2"));

        var result = await new SearchUserChatsQueryHandler(_repository, _currentUser).Handle(
            new SearchUserChatsQuery(ConversationView.Archived, true, true, "budget", ConversationSort.Alphabetical, "cursor-1", 25),
            CancellationToken.None);

        result.NextCursor.Should().Be("cursor-2");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(chat.Id);
        result.Items[0].IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => new SearchUserChatsQueryHandler(_repository, _currentUser)
            .Handle(new SearchUserChatsQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
