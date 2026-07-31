using MediatR;

namespace AskLucy.Application.Ai.Commands.SetAiProviderCredential;

/// <summary>contracts/admin.md `PUT /api/v1/admin/ai/providers/{id}/credential`. The plaintext key never survives past this command's handler.</summary>
public sealed record SetAiProviderCredentialCommand(Guid ProviderId, string ApiKey) : IRequest;
