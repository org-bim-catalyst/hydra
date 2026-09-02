using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetUserAiPreference;

/// <summary>
/// Which provider/model a new conversation starts on. This is now an administrator decision, not
/// a per-user one: the chat default moved out of Settings into the admin panel, where it is
/// configured exactly like every other capability — assign a provider, and the model follows from
/// that provider's default.
/// <para>
/// The stored <c>UserAiPreference</c> is deliberately no longer consulted. Honouring it here
/// while the UI to change it no longer exists would leave whoever happened to save one before the
/// move pinned to it forever, with no way to tell and no way out — the same class of invisible,
/// unchosen routing that the capability assignments exist to end.
/// </para>
/// </summary>
public sealed class GetUserAiPreferenceQueryHandler(
    AiCapabilityProviderResolver capabilityProviderResolver,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetUserAiPreferenceQuery, UserAiPreferenceDto>
{
    public async Task<UserAiPreferenceDto> Handle(GetUserAiPreferenceQuery request, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.Chat, cancellationToken);

        var generationParameters = resolved.GenerationParametersJson is null
            ? null
            : JsonSerializer.Deserialize<GenerationParametersDto>(resolved.GenerationParametersJson);

        return new UserAiPreferenceDto(resolved.ProviderId, resolved.ModelId, generationParameters, resolved.IsPlatformDefault);
    }
}
