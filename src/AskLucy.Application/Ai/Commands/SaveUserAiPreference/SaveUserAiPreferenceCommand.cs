using AskLucy.Application.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SaveUserAiPreference;

/// <summary>contracts/preferences.md `PUT /api/v1/ai/preferences`. Only affects new conversations going forward — never retroactively changes an existing conversation's provider/model.</summary>
public sealed record SaveUserAiPreferenceCommand(
    Guid DefaultProviderId, Guid DefaultModelId, GenerationParametersDto? DefaultGenerationParameters) : IRequest<UserAiPreferenceDto>;
