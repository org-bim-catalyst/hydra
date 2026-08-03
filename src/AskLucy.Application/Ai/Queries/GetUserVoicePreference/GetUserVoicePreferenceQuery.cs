using AskLucy.Application.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetUserVoicePreference;

/// <summary>contracts/voice-preferences.md `GET /api/v1/ai/voice/preferences`.</summary>
public sealed record GetUserVoicePreferenceQuery : IRequest<UserVoicePreferenceDto>;
