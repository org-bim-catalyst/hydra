using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminVoiceProviders;

/// <summary>specs/070 contracts/admin-voice.md `GET /api/v1/admin/voice/providers` — every configured voice provider, Lucy's voice first.</summary>
public sealed record GetAdminVoiceProvidersQuery : IRequest<IReadOnlyList<AdminVoiceProviderDto>>;
