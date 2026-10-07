using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// The three specs/067 scale suites (T224, T232, T234) share one database, one in-process delivery worker per host and
/// wall-clock assertions, so they run alone: another test's host picking up their deliveries, or loading the database
/// while they time it, would make them meaningless.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NotificationScaleTestGroup
{
    public const string Name = "Notification scale tests";
}


/// <summary>Shared helpers for the notification scale suites.</summary>
internal static class NotificationScaleTestSupport
{
    /// <summary>
    /// Shrinks every suite for a local smoke run, without touching its structure: <c>NOTIFICATION_SCALE_FRACTION=0.01</c>
    /// runs 1% of the volumes. Unset (or 1) is the full SC-001/002/004/005 scale. Values above 1 are ignored.
    /// </summary>
    public const string FractionVariable = "NOTIFICATION_SCALE_FRACTION";

    public static double Fraction =>
        double.TryParse(Environment.GetEnvironmentVariable(FractionVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f is > 0 and <= 1
            ? f
            : 1.0;

    public static int Scaled(int full, int minimum) => Math.Max(minimum, (int)Math.Round(full * Fraction));

    /// <summary>Nearest-rank percentile of the samples (0 &lt; p &lt;= 1).</summary>
    public static double Percentile(IReadOnlyCollection<double> samples, double p)
    {
        var ordered = samples.Order().ToList();
        return ordered[Math.Clamp((int)Math.Ceiling(p * ordered.Count) - 1, 0, ordered.Count - 1)];
    }

    /// <summary>Creates confirmed users whose email addresses carry the run id, so cleanup and correlation are exact.</summary>
    public static async Task<List<string>> SeedUsersAsync(IServiceProvider services, string prefix, string run, int count)
    {
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var ids = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var email = $"{prefix}-{run}-{i}@tests.asklucy.io";
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
            (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
            ids.Add(user.Id);
        }

        return ids;
    }

    /// <summary>Removes everything a run created: its notifications (their deliveries cascade), its outbox events and its users.</summary>
    public static async Task CleanupAsync(IServiceProvider services, IReadOnlyCollection<string> userIds, string? eventKeyPrefix)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId != null && userIds.Contains(n.RecipientUserId)).ExecuteDeleteAsync(CancellationToken.None);
        if (eventKeyPrefix is not null)
        {
            await db.NotificationOutboxEvents.Where(e => e.EventKey != null && e.EventKey.StartsWith(eventKeyPrefix)).ExecuteDeleteAsync(CancellationToken.None);
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in userIds)
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is not null)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    /// <summary>
    /// Replaces the shared fake SMTP server for one host, with ConfigureTestServices (which runs after the app's and the
    /// base factory's registrations), as <see cref="AccountEmailLatencyFactory"/> does.
    /// </summary>
    public static void UseMailServer(this IWebHostBuilder builder, IEmailSender sender) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(sender);
        });
}


/// <summary>
/// A healthy fake mail server that stamps each message the instant the worker hands it over, keyed by a marker the
/// test planted in the message (a notification variable).
/// </summary>
public sealed class StampingMailServer(Regex marker) : IEmailSender
{
    public ConcurrentDictionary<string, long> HandedOffAt { get; } = new(StringComparer.Ordinal);

    public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var match = marker.Match(message.TextBody);
        if (match.Success)
        {
            HandedOffAt.TryAdd(match.Value, Stopwatch.GetTimestamp());
        }

        return Task.CompletedTask;
    }
}


/// <summary>
/// A minimal SignalR client for <c>/hubs/notifications</c>: a real WebSocket connection to the in-memory server,
/// speaking the hub's JSON protocol (handshake, then <c>0x1E</c>-terminated frames), authenticated with the same
/// <c>access_token</c> query parameter the browser uses. It stamps each <c>notificationCreated</c> frame at arrival,
/// keyed by a marker in the pushed message. Written against the wire protocol rather than adding a SignalR client
/// package to the test project: the whole server path (dispatcher, pusher, hub context, group, transport) is real.
/// </summary>
public sealed class HubPushListener : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';

    private readonly WebSocket _socket;
    private readonly Regex _marker;
    private readonly CancellationTokenSource _stop = new();
    private Task _receiveLoop = Task.CompletedTask;

    private HubPushListener(WebSocket socket, Regex marker)
    {
        _socket = socket;
        _marker = marker;
    }

    public ConcurrentDictionary<string, long> ReceivedAt { get; } = new(StringComparer.Ordinal);

    public static async Task<HubPushListener> ConnectAsync(TestServer server, string accessToken, Regex marker, CancellationToken ct)
    {
        var client = server.CreateWebSocketClient();
        var socket = await client.ConnectAsync(new Uri($"ws://localhost/hubs/notifications?access_token={Uri.EscapeDataString(accessToken)}"), ct);
        var listener = new HubPushListener(socket, marker);

        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}" + RecordSeparator), WebSocketMessageType.Text, true, ct);

        // The server answers the handshake with "{}" (or {"error": ...}); frames after it are hub messages.
        var reader = new FrameReader(socket);
        var handshake = await reader.ReadFrameAsync(ct) ?? throw new InvalidOperationException("The hub closed the connection during the handshake.");
        using (var doc = JsonDocument.Parse(handshake))
        {
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"The hub handshake was refused: {error.GetString()}");
            }
        }

        listener._receiveLoop = listener.ReceiveAsync(reader);
        return listener;
    }

    private async Task ReceiveAsync(FrameReader reader)
    {
        while (!_stop.IsCancellationRequested)
        {
            var frame = await reader.ReadFrameAsync(_stop.Token);
            if (frame is null)
            {
                return;
            }

            using var doc = JsonDocument.Parse(frame);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetInt32() == 1
                && root.TryGetProperty("target", out var target) && target.GetString() == "notificationCreated")
            {
                var match = _marker.Match(frame);
                if (match.Success)
                {
                    ReceivedAt.TryAdd(match.Value, Stopwatch.GetTimestamp());
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _receiveLoop;
        }
        catch (OperationCanceledException)
        {
            // Expected: the receive loop was cancelled by this disposal.
        }
        catch (WebSocketException)
        {
            // Expected: the server side closed while the loop was reading.
        }

        _socket.Dispose();
        _stop.Dispose();
    }

    /// <summary>Splits the stream into 0x1E-terminated text frames; null when the peer closed.</summary>
    private sealed class FrameReader(WebSocket socket)
    {
        private readonly StringBuilder _pending = new();
        private readonly byte[] _buffer = new byte[16 * 1024];

        public async Task<string?> ReadFrameAsync(CancellationToken ct)
        {
            while (true)
            {
                var text = _pending.ToString();
                var end = text.IndexOf(RecordSeparator, StringComparison.Ordinal);
                if (end >= 0)
                {
                    _pending.Remove(0, end + 1);
                    return text[..end];
                }

                var result = await socket.ReceiveAsync(_buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                _pending.Append(Encoding.UTF8.GetString(_buffer, 0, result.Count));
            }
        }
    }
}
