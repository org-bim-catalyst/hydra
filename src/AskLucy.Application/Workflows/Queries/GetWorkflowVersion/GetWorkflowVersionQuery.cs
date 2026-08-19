using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowVersion;

public sealed record GetWorkflowVersionQuery(Guid WorkflowId, int VersionNumber) : IRequest<WorkflowVersionDto>;
