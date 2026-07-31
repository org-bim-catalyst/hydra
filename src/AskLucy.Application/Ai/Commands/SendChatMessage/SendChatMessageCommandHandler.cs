using AskLucy.Application.Abstractions;
using FluentValidation;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// MediatR's IPipelineBehavior validation pipeline covers ordinary requests only —
/// stream requests validate inline here (a dedicated stream pipeline behavior would be
/// over-engineering for the single stream request this migration has, per Simplicity/YAGNI).
/// Resolves the provider by key (specs/005-multi-provider-ai-engine, research.md Decision 3)
/// instead of depending on a single injected <see cref="IAIProvider"/> — this is the seam that
/// makes provider switching a configuration/catalog choice, not a code change.
/// </summary>
public sealed class SendChatMessageCommandHandler(
    IAIProviderResolver providerResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IValidator<SendChatMessageCommand> validator) : IStreamRequestHandler<SendChatMessageCommand, StreamChunk>
{
    public async IAsyncEnumerable<StreamChunk> Handle(
        SendChatMessageCommand request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        // The validator already confirmed these exist/are enabled/available — re-fetching
        // here (rather than threading the entities through) keeps the validator's job
        // purely "is this request valid" and the handler's job purely "execute it".
        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var aiProvider = providerResolver.Resolve(provider.ProviderKey);

        var messages = request.Messages
            .Select(m => new ChatMessage(ParseRole(m.Role), m.Content))
            .ToList();

        await foreach (var chunk in aiProvider.StreamChatAsync(messages, model.ModelKey, request.GenerationParameters, cancellationToken))
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
