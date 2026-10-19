using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Ai.Queries.GetProviderHealthRetention;

public sealed class GetProviderHealthRetentionQueryHandler(
    IProviderHealthRetentionRepository settings,
    IOptions<ProviderHealthCheckOptions> options)
    : IRequestHandler<GetProviderHealthRetentionQuery, ProviderHealthRetentionDto>
{
    public async Task<ProviderHealthRetentionDto> Handle(GetProviderHealthRetentionQuery request, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync(cancellationToken);
        var intervalMinutes = options.Value.Interval.TotalMinutes;

        return stored is null
            ? new ProviderHealthRetentionDto(
                options.Value.RetentionDays,
                intervalMinutes,
                ProviderHealthRetentionSource.Configuration,
                RowVersion: null)
            : new ProviderHealthRetentionDto(
                stored.RetentionDays,
                intervalMinutes,
                ProviderHealthRetentionSource.Stored,
                Convert.ToBase64String(stored.RowVersion));
    }
}
