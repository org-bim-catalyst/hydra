using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SaveUserAiPreference;

/// <summary>data-model.md: <see cref="UserAiPreference"/> is created lazily on first save, not at registration.</summary>
public sealed class SaveUserAiPreferenceCommandHandler(
    IUserAiPreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<SaveUserAiPreferenceCommand, UserAiPreferenceDto>
{
    public async Task<UserAiPreferenceDto> Handle(SaveUserAiPreferenceCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var preference = await preferences.GetByUserIdAsync(userId, cancellationToken);

        var generationParametersJson = request.DefaultGenerationParameters is null
            ? null
            : JsonSerializer.Serialize(request.DefaultGenerationParameters);

        if (preference is null)
        {
            preference = UserAiPreference.Create(userId, userId);
            preferences.Add(preference);
        }

        preference.SetDefaults(request.DefaultProviderId, request.DefaultModelId, generationParametersJson, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UserAiPreferenceDto(request.DefaultProviderId, request.DefaultModelId, request.DefaultGenerationParameters, IsPlatformDefault: false);
    }
}
