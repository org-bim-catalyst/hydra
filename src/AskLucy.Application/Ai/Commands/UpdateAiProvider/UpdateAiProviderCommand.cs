using MediatR;

namespace AskLucy.Application.Ai.Commands.UpdateAiProvider;

/// <summary>contracts/admin.md `PATCH /api/v1/admin/ai/providers/{id}` — both fields optional, only supplied ones change.</summary>
public sealed record UpdateAiProviderCommand(Guid ProviderId, bool? IsEnabled, Guid? DefaultModelId) : IRequest;
