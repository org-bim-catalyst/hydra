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
    private readonly IEmailTemplateRenderer _templateRenderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly RequestEmailChangeCommandHandler _handler;

    public RequestEmailChangeCommandHandlerTests()
    {
        var appOptions = Microsoft.Extensions.Options.Options.Create(new AppOptions { FrontendBaseUrl = "https://tests.asklucy.io" });
        _templateRenderer.Render(Arg.Any<AccountEmailContent>()).Returns(("<html-body>", "text-body"));
        _handler = new RequestEmailChangeCommandHandler(_identityService, _templateRenderer, _emailSender, appOptions);
    }

    [Fact]
    public async Task Handle_ShouldEmailTheNewAddress_WithAConfirmationLinkContainingTheToken()
    {
        _identityService.GenerateChangeEmailTokenAsync("user-1", "new@example.com", Arg.Any<CancellationToken>())
            .Returns("change-token");

        AccountEmailContent? capturedContent = null;
        _templateRenderer.When(x => x.Render(Arg.Any<AccountEmailContent>()))
            .Do(call => capturedContent = call.Arg<AccountEmailContent>());

        await _handler.Handle(new RequestEmailChangeCommand("user-1", "new@example.com"), CancellationToken.None);

        capturedContent.Should().NotBeNull();
        capturedContent!.PrimaryAction.Should().NotBeNull();
        capturedContent.PrimaryAction!.Url.Should().Contain("confirm-email-change");
        capturedContent.PrimaryAction!.Url.Should().Contain("change-token");
        capturedContent.PrimaryAction!.Url.Should().Contain("user-1");
        await _emailSender.Received(1).SendAsync(
            "new@example.com",
            Arg.Any<string>(),
            "<html-body>",
            "text-body",
            Arg.Any<CancellationToken>());
    }
}
