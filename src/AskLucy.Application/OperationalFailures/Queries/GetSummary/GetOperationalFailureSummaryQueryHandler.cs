using AskLucy.Application.OperationalFailures.Abstractions;
using MediatR;

namespace AskLucy.Application.OperationalFailures.Queries.GetSummary;

public sealed class GetOperationalFailureSummaryQueryHandler(IOperationalFailureStore store)
    : IRequestHandler<GetOperationalFailureSummaryQuery, OperationalFailureSummaryDto>
{
    public async Task<OperationalFailureSummaryDto> Handle(GetOperationalFailureSummaryQuery request, CancellationToken cancellationToken) =>
        new(await store.CountUnacknowledgedCriticalRootCausesAsync(cancellationToken));
}
