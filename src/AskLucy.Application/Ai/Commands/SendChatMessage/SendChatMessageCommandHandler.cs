using AskLucy.Application.Abstractions;
using FluentValidation;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// MediatR's IPipelineBehavior validation pipeline covers ordinary requests only —
/// stream requests validate inline here (a dedicated stream pipeline behavior would be
/// over-engineering for the single stream request this migration has, per Simplicity/YAGNI).
/// </summary>
public sealed class SendChatMessageCommandHandler(
    IAIProvider aiProvider,
    IValidator<SendChatMessageCommand> validator) : IStreamRequestHandler<SendChatMessageCommand, string>
{
    public async IAsyncEnumerable<string> Handle(
        SendChatMessageCommand request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var messages = request.Messages
            .Select(m => new ChatMessage(ParseRole(m.Role), m.Content))
            .ToList();

        await foreach (var chunk in aiProvider.StreamChatAsync(messages, cancellationToken))
        {
            yield return chunk;
        }
    }

    private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
