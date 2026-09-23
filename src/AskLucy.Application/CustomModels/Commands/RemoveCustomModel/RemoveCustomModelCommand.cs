using MediatR;

namespace AskLucy.Application.CustomModels.Commands.RemoveCustomModel;

/// <summary>
/// specs/072 contracts/admin-custom-models.md <c>DELETE {id}</c> (FR-031). Soft-deletes a failed or
/// cancelled model, freeing its name. Files already on the deployment target are left in place.
/// </summary>
public sealed record RemoveCustomModelCommand(Guid Id) : IRequest;
