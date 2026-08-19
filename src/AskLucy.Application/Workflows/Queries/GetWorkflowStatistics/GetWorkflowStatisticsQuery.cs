using AskLucy.Application.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowStatistics;

public sealed record GetWorkflowStatisticsQuery : IRequest<WorkflowStatisticsDto>;
