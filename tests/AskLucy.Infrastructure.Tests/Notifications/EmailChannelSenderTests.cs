using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Notifications.Email;
using FluentAssertions;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// <see cref="EmailChannelSender"/> (specs/067 US3, T114): takes a limiter token, renders, builds the message
/// with its deterministic Message-ID, sends, and turns every outcome into a classified result without throwing,
/// except for a host shutdown.
/// </summary>
public sealed class EmailChannelSenderTests
{
    private static readonly NotificationTypeDefinition WorkflowFailed = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);
    private static readonly Guid DeliveryId = Guid.Parse("0192a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly INotificationTemplateRenderer _renderer = Substitute.For<INotificationTemplateRenderer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly FakeLogger<EmailChannelSender> _logger = new();
    private readonly NotificationsOptions _options = new();
    private readonly Guid _templateVersion = Guid.CreateVersion7();

    public EmailChannelSenderTests()
    {
        _renderer.RenderEmailAsync(Arg.Any<NotificationTypeDefinition>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string?>>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedEmail("Workflow failed", "<p>html</p>", "text", _templateVersion, "en"));
    }

    private EmailChannelSender CreateSut(int maxPerMinute = 600, int reserved = 20)
    {
        var monitor = Substitute.For<IOptionsMonitor<NotificationsOptions>>();
        monitor.CurrentValue.Returns(new NotificationsOptions
        {
            Email = new NotificationEmailOptions { MaxPerMinute = maxPerMinute, ReservedPerMinuteForMandatory = reserved, SendTimeoutSeconds = 60 },
        });
        return new EmailChannelSender(
            new EmailSendRateLimiter(monitor, _time), _renderer, _emailSender,
            Options.Create(new SmtpOptions { FromTransactional = "noreply@bimcatalyst.com" }), monitor, _logger);
    }

    private static DeliveryContext Context(bool mandatory = false, SendProgress? progress = null) => new(
        DeliveryId, Guid.CreateVersion7(), WorkflowFailed, NotificationPriority.High, "en", "layla@example.com",
        new Dictionary<string, string?> { ["workflowName"] = "Nightly" }, mandatory, "corr-1", progress);

    [Fact]
    public async Task Send_RendersAndSends_WithTheDeliveryIdAsTheMessageIdOnTheConfiguredDomain()
    {
        EmailMessage? sent = null;
        await _emailSender.SendAsync(Arg.Do<EmailMessage>(m => sent = m), Arg.Any<CancellationToken>());

        var result = await CreateSut().SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.Sent);
        result.TemplateVersionId.Should().Be(_templateVersion);
        result.Language.Should().Be("en");
        sent.Should().NotBeNull();
        sent!.MessageId.Should().Be("<0192a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b@bimcatalyst.com>");
        sent.To.Should().Be("layla@example.com");
        sent.Subject.Should().Be("Workflow failed");
        sent.HtmlBody.Should().Be("<p>html</p>");
        sent.TextBody.Should().Be("text");
        sent.ReplyTo.Should().BeNull("Reply-To is opt-in and nothing in this feature sets it");
    }

    [Fact]
    public async Task Send_ReportsTransmissionStarting_BeforeTheMessageLeavesTheProcess_ButNotBeforeRendering()
    {
        var progress = new SendProgress();
        var startedWhenRendering = true;
        var startedWhenSending = false;
        _renderer.RenderEmailAsync(Arg.Any<NotificationTypeDefinition>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string?>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                startedWhenRendering = progress.TransmissionStarted;
                return new RenderedEmail("s", "h", "t", _templateVersion, "en");
            });
        await _emailSender.SendAsync(Arg.Do<EmailMessage>(_ => startedWhenSending = progress.TransmissionStarted), Arg.Any<CancellationToken>());

        await CreateSut().SendAsync(Context(progress: progress), CancellationToken.None);

        startedWhenRendering.Should().BeFalse("a shutdown while rendering sends nothing, so that delivery can go back to the queue");
        startedWhenSending.Should().BeTrue();
    }

    [Fact]
    public async Task Send_WithNoLimiterCapacity_DefersWithoutRenderingOrSending()
    {
        var sut = CreateSut(maxPerMinute: 2, reserved: 0);
        await sut.SendAsync(Context(), CancellationToken.None);
        await sut.SendAsync(Context(), CancellationToken.None);
        _emailSender.ClearReceivedCalls();
        _renderer.ClearReceivedCalls();

        var result = await sut.SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.Deferred);
        result.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        await _renderer.DidNotReceiveWithAnyArgs().RenderEmailAsync(default!, default!, default!, TestContext.Current.CancellationToken);
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default(EmailMessage)!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Send_ForAMandatoryType_StillHasCapacity_WhenOptionalMailHasDrainedTheGeneralLane()
    {
        var sut = CreateSut(maxPerMinute: 4, reserved: 2);
        for (var i = 0; i < 2; i++)
        {
            (await sut.SendAsync(Context(mandatory: false), CancellationToken.None)).Outcome.Should().Be(ChannelSendOutcome.Sent);
        }

        (await sut.SendAsync(Context(mandatory: false), CancellationToken.None)).Outcome.Should().Be(ChannelSendOutcome.Deferred);
        (await sut.SendAsync(Context(mandatory: true), CancellationToken.None)).Outcome.Should().Be(ChannelSendOutcome.Sent);
    }

    [Fact]
    public async Task Send_WhenTheTemplateCannotBeRendered_IsAPermanentRenderError_AndNothingIsSent()
    {
        _renderer.RenderEmailAsync(Arg.Any<NotificationTypeDefinition>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string?>>(), Arg.Any<CancellationToken>())
            .Returns<RenderedEmail>(_ => throw new NotificationRenderException("No published Email template exists for 'x'."));

        var result = await CreateSut().SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.PermanentFailure);
        result.FailureKind.Should().Be(DeliveryFailureKind.RenderError);
        result.SafeReason.Should().NotContain("published", "the stored reason is fixed wording, not the exception text");
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default(EmailMessage)!, TestContext.Current.CancellationToken);
        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Error && r.Exception != null);
    }

    [Fact]
    public async Task Send_AFiveHundredReply_IsPermanent()
    {
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 5.1.1 jane@example.com unknown"));

        var result = await CreateSut().SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.PermanentFailure);
        result.FailureKind.Should().Be(DeliveryFailureKind.Permanent);
        result.ProviderResponse.Should().Be("SMTP 550 5.1.1");
        $"{result.SafeReason} {result.ProviderResponse}".Should().NotContain("jane");
    }

    [Fact]
    public async Task Send_AFourHundredReply_IsTransient()
    {
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.MailboxBusy, "450 4.2.0 busy"));

        var result = await CreateSut().SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.TransientFailure);
        result.FailureKind.Should().Be(DeliveryFailureKind.Transient);
        result.RequiresAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Send_ARejectedLogin_IsTransient_AndFlagsTheChannelForAttention()
    {
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new MailKit.Security.AuthenticationException("535 authentication failed"));

        var result = await CreateSut().SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.TransientFailure);
        result.RequiresAttention.Should().BeTrue();
    }

    [Fact]
    public async Task Send_ADroppedConnection_IsTransient_AndTheFailureIsLoggedWithoutTheAddress()
    {
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("The connection was reset"));

        var result = await CreateSut().SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.TransientFailure);
        var entry = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        entry.Message.Should().Contain(DeliveryId.ToString()).And.NotContain("layla@example.com");
    }

    [Fact]
    public async Task Send_ATimeout_IsTransient_WhenOnlyTheSendTimeoutFired()
    {
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(call => Task.FromCanceled(new CancellationToken(canceled: true)));

        var result = await CreateSut().SendAsync(Context(), CancellationToken.None);

        result.Outcome.Should().Be(ChannelSendOutcome.TransientFailure, "the host did not stop; the send simply timed out");
    }

    [Fact]
    public async Task Send_AHostShutdownMidSend_PropagatesInsteadOfBeingClassified()
    {
        using var shutdown = new CancellationTokenSource();
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(call =>
            {
                shutdown.Cancel();
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        var act = () => CreateSut().SendAsync(Context(), shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("noreply@bimcatalyst.com", "bimcatalyst.com")]
    [InlineData("Ask Lucy <no-reply@mail.example.test>", "mail.example.test>")]
    [InlineData("not-an-address", "asklucy.invalid")]
    public void MessageIdFor_UsesTheDomainOfTheConfiguredFromAddress(string from, string expectedDomainPart)
    {
        var id = EmailChannelSender.MessageIdFor(DeliveryId, from);

        id.Should().StartWith("<0192a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b@").And.Contain(expectedDomainPart.TrimEnd('>'));
        EmailChannelSender.MessageIdFor(DeliveryId, from).Should().Be(id, "the same delivery always gets the same id (R5)");
    }
}
