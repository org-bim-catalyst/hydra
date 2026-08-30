using MediatR;

namespace AskLucy.Application.Ai.Commands.UpdateAiProvider;

/// <summary>contracts/admin.md `PATCH /api/v1/admin/ai/providers/{id}` — both fields optional, only supplied ones change.</summary>
/// <summary>
/// <paramref name="ClearDefaultModel"/> exists because a null <paramref name="DefaultModelId"/>
/// already means "leave it alone" — a PATCH that only flips IsEnabled must not wipe the
/// default as a side effect. Clearing therefore needs its own explicit signal, and it is a
/// real operation: DefaultProviderResolver picks the first enabled provider *in display-name
/// order* that has a default set, so removing one provider's default is how an administrator
/// hands the platform default to another.
/// </summary>
public sealed record UpdateAiProviderCommand(
    Guid ProviderId, bool? IsEnabled, Guid? DefaultModelId, bool ClearDefaultModel = false) : IRequest;
