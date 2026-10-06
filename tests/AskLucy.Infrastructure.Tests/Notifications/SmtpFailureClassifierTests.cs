using System.IO;
using System.Net.Sockets;
using AskLucy.Infrastructure.Notifications.Email;
using FluentAssertions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// T103 — <see cref="SmtpFailureClassifier"/> (research R6): a 5xx reply is permanent; a 4xx, a timeout or a
/// dropped connection is transient; a rejected login is transient but raises the channel alert; and the
/// stored reason never carries a credential, an address or the server's own banner (FR-056).
/// </summary>
public sealed class SmtpFailureClassifierTests
{
    private const string Banner = "550 5.1.1 <jane.doe@example.com> mx7.mailhost.example ESMTP Postfix (Ubuntu) user=sa password=Hunter2!";

    private static SmtpCommandException Command(SmtpStatusCode code, string message = Banner) =>
        new(SmtpErrorCode.RecipientNotAccepted, code, message);

    [Theory]
    [InlineData(SmtpStatusCode.MailboxUnavailable)]            // 550
    [InlineData(SmtpStatusCode.UserNotLocalTryAlternatePath)]                // 551
    [InlineData(SmtpStatusCode.ExceededStorageAllocation)]     // 552
    [InlineData(SmtpStatusCode.MailboxNameNotAllowed)]         // 553
    [InlineData(SmtpStatusCode.TransactionFailed)]             // 554
    public void Classify_FiveHundredReply_IsPermanent(SmtpStatusCode code)
    {
        var failure = SmtpFailureClassifier.Classify(Command(code));

        failure.Class.Should().Be(SmtpFailureClass.Permanent);
        failure.RequiresAttention.Should().BeFalse();
    }

    [Theory]
    [InlineData(SmtpStatusCode.ServiceNotAvailable)]           // 421
    [InlineData(SmtpStatusCode.MailboxBusy)]                   // 450
    [InlineData(SmtpStatusCode.ErrorInProcessing)]        // 451
    [InlineData(SmtpStatusCode.InsufficientStorage)]           // 452
    public void Classify_FourHundredReply_IsTransient(SmtpStatusCode code)
    {
        var failure = SmtpFailureClassifier.Classify(Command(code));

        failure.Class.Should().Be(SmtpFailureClass.Transient);
        failure.RequiresAttention.Should().BeFalse();
    }

    public static TheoryData<Exception> TransientTransportFailures() => new()
    {
        new OperationCanceledException("The operation timed out."),
        new TimeoutException(),
        new SocketException((int)SocketError.ConnectionReset),
        new IOException("Unable to read data from the transport connection."),
        new SmtpProtocolException("The SMTP server unexpectedly disconnected."),
        new ServiceNotConnectedException("The SmtpClient is not connected."),
        new SslHandshakeException("The remote certificate is invalid.", new InvalidOperationException("tls")),
    };

    [Theory]
    [MemberData(nameof(TransientTransportFailures))]
    public void Classify_TimeoutOrDroppedConnection_IsTransient(Exception exception)
    {
        var failure = SmtpFailureClassifier.Classify(exception);

        failure.Class.Should().Be(SmtpFailureClass.Transient);
        failure.RequiresAttention.Should().BeFalse();
    }

    [Fact]
    public void Classify_AuthenticationFailure_IsTransientAndRaisesTheHealthAlert()
    {
        var failure = SmtpFailureClassifier.Classify(new AuthenticationException("535: authentication failed for user sa"));

        failure.Class.Should().Be(SmtpFailureClass.Transient);
        failure.RequiresAttention.Should().BeTrue();
    }

    [Theory]
    [InlineData(SmtpStatusCode.AuthenticationRequired)]        // 530
    [InlineData(SmtpStatusCode.AuthenticationInvalidCredentials)] // 535
    public void Classify_AuthenticationReplyCode_IsTransientAndRaisesTheHealthAlert(SmtpStatusCode code)
    {
        var failure = SmtpFailureClassifier.Classify(Command(code));

        failure.Class.Should().Be(SmtpFailureClass.Transient);
        failure.RequiresAttention.Should().BeTrue();
    }

    [Fact]
    public void Classify_AnAddressTheMailLibraryRejects_IsPermanent()
    {
        var failure = SmtpFailureClassifier.Classify(new ParseException("Invalid mailbox", 0, 0));

        failure.Class.Should().Be(SmtpFailureClass.Permanent);
    }

    [Fact]
    public void Classify_AnUnknownException_IsTransientSoItIsRetriedRatherThanLost()
    {
        var failure = SmtpFailureClassifier.Classify(new InvalidOperationException("boom"));

        failure.Class.Should().Be(SmtpFailureClass.Transient);
    }

    public static TheoryData<Exception> AllClassifiedFailures() => new()
    {
        Command(SmtpStatusCode.MailboxUnavailable),
        Command(SmtpStatusCode.MailboxBusy),
        Command(SmtpStatusCode.AuthenticationInvalidCredentials),
        new AuthenticationException("535 5.7.8 authentication failed for sa / Hunter2!"),
        new SmtpProtocolException($"The server said: {Banner}"),
        new IOException(Banner),
        new InvalidOperationException(Banner),
    };

    [Theory]
    [MemberData(nameof(AllClassifiedFailures))]
    public void Classify_NeverPutsACredentialAnAddressOrTheBannerInTheStoredText(Exception exception)
    {
        var failure = SmtpFailureClassifier.Classify(exception);

        var stored = $"{failure.SafeReason} {failure.ProviderResponse}";
        stored.Should().NotContain("Hunter2").And.NotContain("password")
            .And.NotContain("jane.doe").And.NotContain("example.com")
            .And.NotContain("Postfix").And.NotContain("mx7");
    }

    [Fact]
    public void Classify_ProviderResponse_IsTheReplyCodeAndEnhancedStatusOnly()
    {
        var failure = SmtpFailureClassifier.Classify(Command(SmtpStatusCode.MailboxUnavailable));

        failure.ProviderResponse.Should().Be("SMTP 550 5.1.1");
    }
}
