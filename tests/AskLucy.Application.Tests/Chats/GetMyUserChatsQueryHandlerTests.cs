using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Queries.GetMyUserChats;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>Closes the legacy cross-user enumeration gap (FR-018, User Story 3).</summary>
public sealed class GetMyUserChatsQueryHandlerTests
{
    private readonly IUserChatRepository _repository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldOnlyRequestChatsForTheCallingUser()
    {
        _currentUser.UserId.Returns("user-1");
        var ownChat = UserChat.Create("Mine", "user-1", null, "user-1");
        _repository.ListByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns([ownChat]);

        var handler = new GetMyUserChatsQueryHandler(_repository, _currentUser);
        var result = await handler.Handle(new GetMyUserChatsQuery(), CancellationToken.None);

        result.Should().ContainSingle(c => c.Title == "Mine");
        await _repository.DidNotReceive().ListByUserIdAsync(
            Arg.Is<string>(id => id != "user-1"), Arg.Any<CancellationToken>());
    }
}
