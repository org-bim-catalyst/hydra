using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Notifications.Email;
using FluentAssertions;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>
/// The hub's send through the real MailKit client against a loopback server (specs/067 T110, research R5, R6):
/// the Message-ID the caller chose reaches the wire, <c>From</c> and the envelope sender come only from
/// <see cref="SmtpOptions"/>, one authenticated connection serves a batch, and a failed send surfaces as the
/// exception <see cref="SmtpFailureClassifier"/> classifies, with the connection dropped so it is never reused.
/// </summary>
public sealed class SmtpEmailSenderTests : IAsyncDisposable
{
    private readonly FakeSmtpServer _server = new();
    private readonly SmtpConnectionHolder _connections;
    private readonly SmtpEmailSender _sender;

    public SmtpEmailSenderTests()
    {
        var smtp = Options.Create(new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = _server.Port,
            UseSsl = false,
            UseStartTls = false,
            FromName = "Ask Lucy",
            FromTransactional = "noreply@bimcatalyst.com",
        });
        var monitor = Substitute.For<IOptionsMonitor<NotificationsOptions>>();
        monitor.CurrentValue.Returns(new NotificationsOptions { Email = new NotificationEmailOptions { SendTimeoutSeconds = 5 } });
        _connections = new SmtpConnectionHolder(smtp, monitor, TimeProvider.System, NullLogger<SmtpConnectionHolder>.Instance);
        _sender = new SmtpEmailSender(smtp, _connections);
    }

    public async ValueTask DisposeAsync()
    {
        await _connections.DisposeAsync();
        await _server.DisposeAsync();
    }

    private static EmailMessage Message(string to = "layla@example.com", string messageId = "<0192a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b@bimcatalyst.com>", string? replyTo = null) =>
        new(to, "Workflow failed", "<p>Hello</p>", "Hello", messageId, replyTo);

    [Fact]
    public async Task Send_PutsTheCallersMessageIdOnTheWire_AndSendsFromTheConfiguredAddressOnly()
    {
        await _sender.SendAsync(Message(), TestContext.Current.CancellationToken);

        var received = _server.Messages.Should().ContainSingle().Subject;
        received.Data.Should().ContainEquivalentOf("Message-ID: <0192a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b@bimcatalyst.com>");
        received.Data.Should().Contain("Subject: Workflow failed").And.Contain("Hello");
        received.Data.Should().Contain("From: Ask Lucy <noreply@bimcatalyst.com>");
        received.MailFrom.Should().Contain("noreply@bimcatalyst.com");
        received.RcptTo.Should().ContainSingle().Which.Should().Contain("layla@example.com");
    }

    [Fact]
    public async Task Send_SendsBothAnHtmlAndAPlainTextPart()
    {
        await _sender.SendAsync(Message(), TestContext.Current.CancellationToken);

        var data = _server.Messages.Single().Data;
        data.Should().Contain("multipart/alternative").And.Contain("text/plain").And.Contain("text/html");
    }

    [Fact]
    public async Task Send_AValidatedReplyTo_IsAddedButNeverBecomesTheSender()
    {
        await _sender.SendAsync(Message(replyTo: "help@example.com"), TestContext.Current.CancellationToken);

        var received = _server.Messages.Single();
        received.Data.Should().Contain("Reply-To: help@example.com");
        received.MailFrom.Should().NotContain("help@example.com");
    }

    [Fact]
    public async Task Send_ABatch_ReusesOneConnection()
    {
        for (var i = 0; i < 5; i++)
        {
            await _sender.SendAsync(Message(messageId: $"<{Guid.NewGuid()}@bimcatalyst.com>"), TestContext.Current.CancellationToken);
        }

        _server.Messages.Should().HaveCount(5);
        _server.ConnectionCount.Should().Be(1, "a worker batch shares one authenticated connection instead of a handshake per message");
    }

    [Fact]
    public async Task Send_ARejectedRecipient_ThrowsAFiveHundredReply_ThatClassifiesAsPermanent_AndTheNextSendStillWorks()
    {
        _server.ErrorFor = (verb, occurrence) => verb == "RCPT" && occurrence == 1 ? "550 5.1.1 No such user" : null;

        var act = () => _sender.SendAsync(Message("nobody@example.com"), TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<Exception>()).Which;
        SmtpFailureClassifier.Classify(failure).Class.Should().Be(SmtpFailureClass.Permanent);

        await _sender.SendAsync(Message(), TestContext.Current.CancellationToken);
        _server.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task Send_ATemporaryReply_ThrowsAFourHundredReply_ThatClassifiesAsTransient()
    {
        _server.ErrorFor = (verb, _) => verb == "RCPT" ? "451 4.3.0 Try again later" : null;

        var act = () => _sender.SendAsync(Message(), TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<Exception>()).Which;
        SmtpFailureClassifier.Classify(failure).Class.Should().Be(SmtpFailureClass.Transient);
    }

    [Fact]
    public async Task Send_ADroppedConnectionMidConversation_IsTransient_AndTheNextSendReconnects()
    {
        _server.DropOn = (verb, occurrence) => verb == "DATA" && occurrence == 1;

        var act = () => _sender.SendAsync(Message(), TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<Exception>()).Which;
        SmtpFailureClassifier.Classify(failure).Class.Should().Be(SmtpFailureClass.Transient);

        _server.DropOn = static (_, _) => false;
        await _sender.SendAsync(Message(), TestContext.Current.CancellationToken);
        _server.ConnectionCount.Should().Be(2, "a connection that failed is never reused");
        _server.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task Send_AConnectionTheServerClosedWhileIdle_IsDetectedAndReplaced()
    {
        await _sender.SendAsync(Message(messageId: "<a@bimcatalyst.com>"), TestContext.Current.CancellationToken);
        await _server.DisposeAsync();
        var server2 = new FakeSmtpServer();
        try
        {
            var smtp = Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = server2.Port, UseSsl = false, UseStartTls = false, FromTransactional = "noreply@bimcatalyst.com" });
            var monitor = Substitute.For<IOptionsMonitor<NotificationsOptions>>();
            monitor.CurrentValue.Returns(new NotificationsOptions { Email = new NotificationEmailOptions { SendTimeoutSeconds = 5 } });
            await using var second = new SmtpConnectionHolder(smtp, monitor, TimeProvider.System, NullLogger<SmtpConnectionHolder>.Instance);

            await new SmtpEmailSender(smtp, second).SendAsync(Message(messageId: "<b@bimcatalyst.com>"), TestContext.Current.CancellationToken);

            server2.Messages.Should().ContainSingle();
        }
        finally
        {
            await server2.DisposeAsync();
        }
    }

    [Fact]
    public async Task Send_AnInvalidRecipientAddress_ThrowsSomethingThatClassifiesAsPermanent_BeforeAnythingIsSent()
    {
        var act = () => _sender.SendAsync(Message("not an address"), TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<Exception>()).Which;
        SmtpFailureClassifier.Classify(failure).Class.Should().Be(SmtpFailureClass.Permanent);
        _server.ConnectionCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_AnAttachment_IsStreamedAtSendTime_AndItsStreamIsClosedAfterwards()
    {
        var stream = new MemoryStream("attachment-content"u8.ToArray());
        var message = Message() with
        {
            Attachments = [new EmailAttachment("notes.txt", "text/plain", _ => Task.FromResult<Stream>(stream))],
        };

        await _sender.SendAsync(message, TestContext.Current.CancellationToken);

        _server.Messages.Single().Data.Should().Contain("filename=notes.txt").And.Contain("attachment-content");
        stream.CanRead.Should().BeFalse("the sender disposes what it opened");
    }
}
