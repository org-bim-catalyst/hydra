using System.Collections.Concurrent;
using System.Globalization;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace AskLucy.Web.Tests;

/// <summary>
/// Keeps the last few thousand rendered log lines of every host in the test process, so a test can assert
/// what was, and was not, written to the log (specs/067 FR-009d: no token or link in any log; FR-009e: only a
/// hash of an address that may belong to nobody). Wired in by <see cref="CustomWebApplicationFactory"/>
/// through Serilog's own configuration, because Serilog replaces the host's logging providers.
/// </summary>
public sealed class CapturedLogSink : ILogEventSink
{
    private const int Capacity = 20_000;

    private readonly ConcurrentQueue<string> _lines = new();

    public static CapturedLogSink Shared { get; } = new();

    /// <summary>A snapshot of the captured lines, oldest first, each the rendered message followed by any exception text.</summary>
    public IReadOnlyList<string> Lines => [.. _lines];

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _lines.Enqueue(logEvent.RenderMessage(CultureInfo.InvariantCulture) + (logEvent.Exception is { } ex ? $"\n{ex}" : string.Empty));
        while (_lines.Count > Capacity && _lines.TryDequeue(out _))
        {
        }
    }
}

/// <summary>The <c>CapturedLogs</c> sink Serilog's configuration binder looks for (<c>Serilog:Using</c> names this assembly).</summary>
public static class CapturedLogSinkExtensions
{
    public static LoggerConfiguration CapturedLogs(this LoggerSinkConfiguration sinkConfiguration)
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        return sinkConfiguration.Sink(CapturedLogSink.Shared);
    }
}
