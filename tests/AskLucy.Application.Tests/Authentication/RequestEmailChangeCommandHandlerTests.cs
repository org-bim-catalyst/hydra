using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.ChangeEmail;
using FluentAssertions;
using NSubstitute;
using Xunit;
using AppOptions = AskLucy.Application.Options.AppOptions;

namespace AskLucy.Application.Tests.Authentication;

public sealed class RequestEmailChangeCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly RequestEmailChangeCommandHandler _handler;

    public RequestEmailChangeCommandHandlerTests()
    {
        var appOptions = Microsoft.Extensions.Options.Options.Create(new AppOptions { FrontendBaseUrl = "https://tests.asklucy.io" });
        _handler = new RequestEmailChangeCommandHandler(_identityService, _emailSender, appOptions);
    }

    [Fact]
    public async Task Handle_ShouldEmailTheNewAddress_WithAConfirmationLinkContainingTheToken()
    {
        _identityService.GenerateChangeEmailTokenAsync("user-1", "new@example.com", Arg.Any<CancellationToken>())
            .Returns("change-token");

        await _handler.Handle(new RequestEmailChangeCommand("user-1", "new@example.com"), CancellationToken.None);

        await _emailSender.Received(1).SendAsync(
            "new@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body != null
                && body.Contains("confirm-email-change")
                && body.Contains("change-token")
                && body.Contains("user-1")),
            Arg.Any<CancellationToken>());
    }
}
