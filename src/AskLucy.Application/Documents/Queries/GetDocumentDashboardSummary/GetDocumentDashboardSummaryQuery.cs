using MediatR;

namespace AskLucy.Application.Documents.Queries.GetDocumentDashboardSummary;

/// <summary>Scoped to the caller only (FR-045) — never another user's documents.</summary>
public sealed record GetDocumentDashboardSummaryQuery : IRequest<DocumentDashboardSummaryDto>;
