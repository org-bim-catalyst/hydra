using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.GetSummary;

/// <summary>specs/074 FR-026 — the admin nav badge: one per root cause with an unacknowledged Critical incident.</summary>
public sealed record GetOperationalFailureSummaryQuery : IRequest<OperationalFailureSummaryDto>;
