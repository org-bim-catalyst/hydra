using AskLucy.Application.Documents.Queries.GetDocumentDashboardSummary;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetOrganizationDashboardSummary;

/// <summary>Aggregated across every user's documents (FR-045a, US6 AC6) — role-gated at the controller (`AdministratorOrSuperUser` policy), never inside this handler. Never exposes individual document content or per-user listings, only aggregate counts/statistics.</summary>
public sealed record GetOrganizationDashboardSummaryQuery : IRequest<DocumentDashboardSummaryDto>;
