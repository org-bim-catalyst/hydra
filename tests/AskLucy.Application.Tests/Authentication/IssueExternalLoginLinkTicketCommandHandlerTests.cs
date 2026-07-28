using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.ExternalLogin;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

public sealed class IssueExternalLoginLinkTicketCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldIssueATicketBoundToTheCurrentUser()
    {
        var codeStore = Substitute.For<IExternalLoginCodeStore>();
        codeStore.Issue("user-1", Arg.Any<TimeSpan>()).Returns("ticket-abc");
        var handler = new IssueExternalLoginLinkTicketCommandHandler(codeStore);

        var ticket = await handler.Handle(new IssueExternalLoginLinkTicketCommand("user-1"), CancellationToken.None);

        ticket.Should().Be("ticket-abc");
    }
}
