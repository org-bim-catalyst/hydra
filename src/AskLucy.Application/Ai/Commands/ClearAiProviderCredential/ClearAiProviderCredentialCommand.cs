using MediatR;

namespace AskLucy.Application.Ai.Commands.ClearAiProviderCredential;

/// <summary>contracts/admin.md `DELETE /api/v1/admin/ai/providers/{id}/credential` — also forces `IsEnabled = false`.</summary>
public sealed record ClearAiProviderCredentialCommand(Guid ProviderId) : IRequest;
