using AskLucy.Application.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetUserAiPreference;

/// <summary>contracts/preferences.md `GET /api/v1/ai/preferences`.</summary>
public sealed record GetUserAiPreferenceQuery : IRequest<UserAiPreferenceDto>;
