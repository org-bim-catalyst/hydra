using MediatR;

namespace AskLucy.Application.CustomModels.Queries.GetDeploymentStatus;

/// <summary>
/// specs/072 contracts/admin-custom-models.md <c>GET deployment-status</c>. Whether deployment is
/// configured and how it will travel — never the host, username, password or root path (FR-017, FR-018).
/// </summary>
public sealed record GetDeploymentStatusQuery : IRequest<DeploymentStatusDto>;
