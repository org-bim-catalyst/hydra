using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.Register;
using FluentAssertions;
using NSubstitute;
using Xunit;
using AppOptions = AskLucy.Application.Options.AppOptions;

namespace AskLucy.Application.Tests.Authentication;

public sealed class RegisterCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IEmailTemplateRenderer _templateRenderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly RegisterCommandHandler _handler;

    public RegisterCommandHandlerTests()
    {
        var appOptions = Microsoft.Extensions.Options.Options.Create(new AppOptions { FrontendBaseUrl = "https://tests.asklucy.io" });
        _templateRenderer.Render(Arg.Any<AccountEmailContent>()).Returns(("<html-body>", "text-body"));
        _handler = new RegisterCommandHandler(_identityService, _templateRenderer, _emailSender, appOptions);
    }

    [Fact]
    public async Task Handle_ShouldSendConfirmationEmail_WhenRegistrationSucceeds()
    {
        _identityService.RegisterAsync("user@example.com", "Password1!", "Ada", "Lovelace", Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1"));
        _identityService.GenerateEmailConfirmationTokenAsync("user-1", Arg.Any<CancellationToken>())
            .Returns("confirmation-token");

        AccountEmailContent? capturedContent = null;
        _templateRenderer.When(x => x.Render(Arg.Any<AccountEmailContent>()))
            .Do(call => capturedContent = call.Arg<AccountEmailContent>());

        var result = await _handler.Handle(
            new RegisterCommand("user@example.com", "Password1!", "Ada", "Lovelace"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Success);
        capturedContent.Should().NotBeNull();
        capturedContent!.PrimaryAction.Should().NotBeNull();
        capturedContent.PrimaryAction!.Url.Should().Contain("confirmation-token");
        capturedContent.PrimaryAction!.Url.Should().Contain("user-1");
        await _emailSender.Received(1).SendAsync(
            "user@example.com",
            Arg.Any<string>(),
            "<html-body>",
            "text-body",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnFailed_AndNeverEmail_WhenIdentityCreationFails()
    {
        _identityService.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Failed, Errors: ["Email already taken"]));

        var result = await _handler.Handle(
            new RegisterCommand("dup@example.com", "Password1!", null, null), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Failed);
        result.Errors.Should().Contain("Email already taken");
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }
}
