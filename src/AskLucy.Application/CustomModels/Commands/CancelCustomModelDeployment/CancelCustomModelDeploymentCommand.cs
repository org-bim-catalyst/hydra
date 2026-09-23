using MediatR;

namespace AskLucy.Application.CustomModels.Commands.CancelCustomModelDeployment;

/// <summary>
/// specs/072 contracts/admin-custom-models.md <c>POST {id}/actions/cancel</c>. A queued deployment
/// is cancelled at once; a running one is flagged and signalled, and its job reports the final
/// <c>Cancelled</c> state once it has stopped (research D7).
/// </summary>
public sealed record CancelCustomModelDeploymentCommand(Guid Id) : IRequest<CustomModelSummaryDto>;
