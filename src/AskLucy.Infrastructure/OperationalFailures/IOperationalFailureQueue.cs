using System.Threading.Channels;
using AskLucy.Application.OperationalFailures.Abstractions;

namespace AskLucy.Infrastructure.OperationalFailures;

/// <summary>
/// The read side of the recorder's channel, for <see cref="OperationalFailureWriterService"/> only
/// (specs/074 research D2). Resolves to the same singleton the recorder writes through.
/// </summary>
internal interface IOperationalFailureQueue
{
    ChannelReader<OperationalFailureSignal> Reader { get; }
}
