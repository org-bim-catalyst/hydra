using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Ai.Commands.UpdateProviderHealthRetention;

public sealed class UpdateProviderHealthRetentionCommandHandler(
    IProviderHealthRetentionRepository settings,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IOptions<ProviderHealthCheckOptions> options,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateProviderHealthRetentionCommand, ProviderHealthRetentionDto>
{
    public async Task<ProviderHealthRetentionDto> Handle(UpdateProviderHealthRetentionCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var setting = await settings.GetAsync(cancellationToken)
            ?? throw new ConcurrencyConflictException("The provider health retention setting row is missing; the migration seeds it.");

        if (!setting.RowVersion.AsSpan().SequenceEqual(request.ExpectedRowVersion))
        {
            throw new ConcurrencyConflictException("The retention window was changed by someone else. Reload it and try again.");
        }

        settings.ExpectRowVersion(setting, request.ExpectedRowVersion);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var previousRetentionDays = setting.RetentionDays;
        setting.Update(request.RetentionDays, actorUserId, utcNow);
        settings.AddAudit(ProviderHealthRetentionAuditLog.Record(actorUserId, previousRetentionDays, setting.RetentionDays, utcNow));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProviderHealthRetentionDto(
            setting.RetentionDays,
            options.Value.Interval.TotalMinutes,
            ProviderHealthRetentionSource.Stored,
            Convert.ToBase64String(setting.RowVersion));
    }
}
