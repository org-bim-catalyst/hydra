using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>specs/061-branded-email-templates T009.</summary>
public sealed class AccountEmailJobTests
{
    private readonly IEmailTemplateRenderer _templateRenderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly AccountEmailJob _job;

    public AccountEmailJobTests()
    {
        _templateRenderer.Render(Arg.Any<AccountEmailContent>()).Returns(("<html-body>", "text-body"));
        var appOptions = Options.Create(new AppOptions { FrontendBaseUrl = "https://tests.asklucy.io" });
        var smtpOptions = Options.Create(new SmtpOptions());
        _job = new AccountEmailJob(
            _templateRenderer, _emailSender, _identityService, appOptions, smtpOptions, NullLogger<AccountEmailJob>.Instance);
    }

    [Fact]
    public async Task ResendConfirmationAsync_ShouldRenderAndSend_WithAConfirmationLinkContainingTheToken()
    {
        _identityService.FindIdByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns("user-1");
        _identityService.GetPasswordResetEligibilityAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility("user@example.com", EmailConfirmed: false, IsLockedOut: false, HasPassword: true));
        _identityService.GenerateEmailConfirmationTokenAsync("user-1", Arg.Any<CancellationToken>()).Returns("fresh-token");

        AccountEmailContent? capturedContent = null;
        _templateRenderer.When(x => x.Render(Arg.Any<AccountEmailContent>()))
            .Do(call => capturedContent = call.Arg<AccountEmailContent>());

        await _job.ResendConfirmationAsync("user@example.com", TestContext.Current.CancellationToken);

        capturedContent.Should().NotBeNull();
        capturedContent!.PrimaryAction.Should().NotBeNull();
        capturedContent.PrimaryAction!.Url.Should().Contain("fresh-token");
        capturedContent.PrimaryAction!.Url.Should().Contain("user-1");
        await _emailSender.Received(1).SendAsync(
            "user@example.com", Arg.Any<string>(), "<html-body>", "text-body", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendConfirmationAsync_ShouldSendNothing_WhenNoAccountExistsForTheAddress()
    {
        _identityService.FindIdByEmailAsync("missing@example.com", Arg.Any<CancellationToken>()).Returns((string?)null);

        await _job.ResendConfirmationAsync("missing@example.com", TestContext.Current.CancellationToken);

        _templateRenderer.DidNotReceiveWithAnyArgs().Render(default!);
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResendConfirmationAsync_ShouldSendNothing_WhenTheAccountIsAlreadyConfirmed()
    {
        _identityService.FindIdByEmailAsync("confirmed@example.com", Arg.Any<CancellationToken>()).Returns("user-2");
        _identityService.GetPasswordResetEligibilityAsync("user-2", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility("confirmed@example.com", EmailConfirmed: true, IsLockedOut: false, HasPassword: true));

        await _job.ResendConfirmationAsync("confirmed@example.com", TestContext.Current.CancellationToken);

        _templateRenderer.DidNotReceiveWithAnyArgs().Render(default!);
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }
}
