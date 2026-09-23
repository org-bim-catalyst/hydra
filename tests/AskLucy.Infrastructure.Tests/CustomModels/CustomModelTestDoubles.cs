using System.Collections.Concurrent;

namespace AskLucy.Infrastructure.Tests.CustomModels;

/// <summary>Answers each request from a responder and keeps every request URI, in order.</summary>
internal sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public ConcurrentQueue<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Enqueue(request.RequestUri!);
        var response = responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

/// <summary>
/// Fires every timer as soon as it's armed and records the due time asked for, so retry backoff and
/// the stall watchdog run instantly and their delays can be asserted.
/// </summary>
internal sealed class ImmediateTimeProvider : TimeProvider
{
    public ConcurrentQueue<TimeSpan> RequestedDelays { get; } = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ImmediateTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ImmediateTimer(ImmediateTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private volatile bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                owner.RequestedDelays.Enqueue(dueTime);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (!_disposed)
                    {
                        callback(state);
                    }
                });
            }

            return true;
        }

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A response body that never delivers a byte until the read is cancelled.</summary>
internal sealed class StallingStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
