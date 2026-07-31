using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminAiProviders;

/// <summary>contracts/admin.md — every provider, enabled or not (FR-003).</summary>
public sealed record GetAdminAiProvidersQuery : IRequest<IReadOnlyList<AdminAiProviderDto>>;
