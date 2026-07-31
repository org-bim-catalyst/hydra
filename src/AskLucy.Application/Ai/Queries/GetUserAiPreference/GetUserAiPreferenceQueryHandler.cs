using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetUserAiPreference;

public sealed class GetUserAiPreferenceQueryHandler(
    IUserAiPreferenceRepository preferences,
    DefaultProviderResolver defaultResolver,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetUserAiPreferenceQuery, UserAiPreferenceDto>
{
    public async Task<UserAiPreferenceDto> Handle(GetUserAiPreferenceQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var preference = await preferences.GetByUserIdAsync(userId, cancellationToken);

        var resolved = await defaultResolver.ResolveAsync(preference, cancellationToken);

        var generationParameters = resolved.GenerationParametersJson is null
            ? null
            : JsonSerializer.Deserialize<GenerationParametersDto>(resolved.GenerationParametersJson);

        return new UserAiPreferenceDto(resolved.ProviderId, resolved.ModelId, generationParameters, resolved.IsPlatformDefault);
    }
}
