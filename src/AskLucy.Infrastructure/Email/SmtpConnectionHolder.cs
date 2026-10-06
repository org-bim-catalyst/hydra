using AskLucy.Application.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// One reusable authenticated SMTP connection for the notification hub's sends (research R6), so a
/// worker batch of emails is not a batch of TLS handshakes and logins against a shared mail host. A
/// singleton guarded by a gate: MailKit's client isn't thread-safe, and one delivery worker sends at a
/// time anyway. A connection that has sat idle is probed before use, and any failure drops it, so a bad
/// connection never serves a second send.
/// </summary>
public sealed partial class SmtpConnectionHolder(
    IOptions<SmtpOptions> smtpOptions,
    IOptionsMonitor<NotificationsOptions> notificationOptions,
    TimeProvider timeProvider,
    ILogger<SmtpConnectionHolder> logger) : IAsyncDisposable
{
    /// <summary>An idle connection older than this is probed before reuse; many hosts drop idle sessions quickly.</summary>
    private static readonly TimeSpan ProbeAfterIdle = TimeSpan.FromSeconds(10);

    /// <summary>Beyond this the connection is simply replaced.</summary>
    private static readonly TimeSpan MaxIdle = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SmtpClient? _client;
    private DateTime _lastUsedUtc;

    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await ConnectedClientAsync(cancellationToken);
            try
            {
                await client.SendAsync(message, cancellationToken);
                _lastUsedUtc = timeProvider.GetUtcNow().UtcDateTime;
            }
            catch
            {
                // A failed or cancelled send leaves the protocol state unknown; never reuse it.
                DropClient();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SmtpClient> ConnectedClientAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (_client is { IsConnected: true } existing)
        {
            var idle = now - _lastUsedUtc;
            if (idle < ProbeAfterIdle)
            {
                return existing;
            }

            if (idle < MaxIdle && await IsAliveAsync(existing, cancellationToken))
            {
                return existing;
            }

            LogReplacingIdleConnection(logger, idle);
            DropClient();
        }
        else
        {
            DropClient();
        }

        var options = smtpOptions.Value;
        var secure = options.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

        var client = new SmtpClient
        {
            Timeout = Math.Max(1, notificationOptions.CurrentValue.Email.SendTimeoutSeconds) * 1000,
        };

        try
        {
            await client.ConnectAsync(options.Host, options.Port, secure, cancellationToken);
            if (!string.IsNullOrEmpty(options.Username))
            {
                await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _lastUsedUtc = now;
        return client;
    }

    private async Task<bool> IsAliveAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.NoOpAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Expected on a dropped idle session: the caller reconnects, which is the whole point of the probe.
            // Logged because a connection that keeps dying between sends points at the mail host.
            LogProbeFailed(logger, ex);
            return false;
        }
    }

    private void DropClient()
    {
        _client?.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_client is { IsConnected: true } client)
            {
                try
                {
                    await client.DisconnectAsync(quit: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Shutting down; the server closes the session on its own.
                    LogDisconnectFailed(logger, ex);
                }
            }

            DropClient();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The idle SMTP connection ({Idle}) is being replaced with a new one.")]
    private static partial void LogReplacingIdleConnection(ILogger logger, TimeSpan idle);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The idle SMTP connection no longer answers; reconnecting.")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SMTP connection could not be closed cleanly on shutdown; the server will end the session.")]
    private static partial void LogDisconnectFailed(ILogger logger, Exception exception);
}
