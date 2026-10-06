using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>
/// A just-enough SMTP server on a loopback port, for exercising the real MailKit sender over a real socket
/// (EHLO, MAIL FROM, RCPT TO, DATA, NOOP, RSET, QUIT, plain text, no TLS). It records every connection and every
/// message it was given, and can be told to answer a command with an error line, or to drop the connection.
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _connections = [];
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _verbCounts = new(StringComparer.Ordinal);
    private int _connectionCount;
    private int _disposed;

    public FakeSmtpServer()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    public List<ReceivedMessage> Messages { get; } = [];

    /// <summary>Replies to a command (by its verb, e.g. <c>RCPT</c>, and its 1-based occurrence across connections) with this line instead of success. Return null to answer normally.</summary>
    public Func<string, int, string?> ErrorFor { get; set; } = static (_, _) => null;

    /// <summary>Closes the socket, without a reply, when this returns true for the verb and its 1-based occurrence.</summary>
    public Func<string, int, bool> DropOn { get; set; } = static (_, _) => false;

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _connectionCount);
                lock (_gate)
                {
                    _connections.Add(Task.Run(() => ServeAsync(client)));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (ObjectDisposedException)
        {
            // Disposed.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            string? from = null;
            var recipients = new List<string>();

            await writer.WriteLineAsync("220 fake.smtp.test ESMTP ready");
            while (await ReadLineAsync(reader) is { } line)
            {
                var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                var occurrence = CountOccurrence(verb);

                if (DropOn(verb, occurrence))
                {
                    return;
                }

                if (ErrorFor(verb, occurrence) is { } error)
                {
                    await writer.WriteLineAsync(error);
                    continue;
                }

                switch (verb)
                {
                    case "EHLO" or "HELO":
                        await writer.WriteLineAsync("250-fake.smtp.test");
                        await writer.WriteLineAsync("250 8BITMIME");
                        break;
                    case "MAIL":
                        from = line;
                        recipients.Clear();
                        await writer.WriteLineAsync("250 2.1.0 OK");
                        break;
                    case "RCPT":
                        recipients.Add(line);
                        await writer.WriteLineAsync("250 2.1.5 OK");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        var data = new StringBuilder();
                        while (await ReadLineAsync(reader) is { } dataLine && dataLine != ".")
                        {
                            data.AppendLine(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine);
                        }

                        lock (_gate)
                        {
                            Messages.Add(new ReceivedMessage(from ?? string.Empty, [.. recipients], data.ToString()));
                        }

                        await writer.WriteLineAsync("250 2.0.0 OK queued");
                        break;
                    case "NOOP" or "RSET":
                        await writer.WriteLineAsync("250 2.0.0 OK");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 2.0.0 Bye");
                        return;
                    default:
                        await writer.WriteLineAsync("502 5.5.2 Command not recognized");
                        break;
                }
            }
        }
    }

    /// <summary>1-based, across every connection: a client that reconnects after a failure doesn't reset the script.</summary>
    private int CountOccurrence(string verb)
    {
        lock (_gate)
        {
            _verbCounts[verb] = _verbCounts.GetValueOrDefault(verb) + 1;
            return _verbCounts[verb];
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader)
    {
        try
        {
            return await reader.ReadLineAsync();
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync();
        _listener.Stop();
        Task[] pending;
        lock (_gate)
        {
            pending = [.. _connections];
        }

        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(TimeSpan.FromSeconds(2)));
        _stop.Dispose();
    }

    internal sealed record ReceivedMessage(string MailFrom, IReadOnlyList<string> RcptTo, string Data);
}
