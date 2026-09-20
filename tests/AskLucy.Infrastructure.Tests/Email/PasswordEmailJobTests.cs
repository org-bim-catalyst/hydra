using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>specs/061-branded-email-templates T010.</summary>
public sealed class PasswordEmailJobTests
{
    private readonly IEmailTemplateRenderer _templateRenderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IPasswordTokenProtector _tokenProtector = Substitute.For<IPasswordTokenProtector>();
    private readonly PasswordEmailJob _job;

    public PasswordEmailJobTests()
    {
        _templateRenderer.Render(Arg.Any<AccountEmailContent>()).Returns(("<html-body>", "text-body"));
        var appOptions = Options.Create(new AppOptions { FrontendBaseUrl = "https://tests.asklucy.io" });
        _job = new PasswordEmailJob(
            _templateRenderer, _emailSender, _tokenProtector, appOptions, NullLogger<PasswordEmailJob>.Instance);
    }

    [Fact]
    public async Task SendResetLinkAsync_ShouldRenderAndSend_WithAResetLinkContainingTheUnprotectedToken()
    {
        _tokenProtector.Unprotect("protected-token").Returns("plaintext-token");

        AccountEmailContent? capturedContent = null;
        _templateRenderer.When(x => x.Render(Arg.Any<AccountEmailContent>()))
            .Do(call => capturedContent = call.Arg<AccountEmailContent>());

        await _job.SendResetLinkAsync("user-1", "user@example.com", "protected-token", TestContext.Current.CancellationToken);

        capturedContent.Should().NotBeNull();
        capturedContent!.PrimaryAction.Should().NotBeNull();
        capturedContent.PrimaryAction!.Url.Should().Contain("plaintext-token");
        capturedContent.PrimaryAction!.Url.Should().Contain("user-1");
        await _emailSender.Received(1).SendAsync(
            "user@example.com", Arg.Any<string>(), "<html-body>", "text-body", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendResetLinkAsync_ShouldThrow_AndNeverEmail_WhenTheTokenCannotBeUnprotected()
    {
        _tokenProtector.Unprotect("bad-token").Returns((string?)null);

        var act = () => _job.SendResetLinkAsync("user-1", "user@example.com", "bad-token", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _templateRenderer.DidNotReceiveWithAnyArgs().Render(default!);
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendPasswordChangedNoticeAsync_ShouldRenderAndSend_WithNoPrimaryAction()
    {
        var changedAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

        AccountEmailContent? capturedContent = null;
        _templateRenderer.When(x => x.Render(Arg.Any<AccountEmailContent>()))
            .Do(call => capturedContent = call.Arg<AccountEmailContent>());

        await _job.SendPasswordChangedNoticeAsync("user@example.com", changedAt, TestContext.Current.CancellationToken);

        capturedContent.Should().NotBeNull();
        capturedContent!.PrimaryAction.Should().BeNull();
        await _emailSender.Received(1).SendAsync(
            "user@example.com", Arg.Any<string>(), "<html-body>", "text-body", Arg.Any<CancellationToken>());
    }
}
