using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.SetAiCapabilityAssignment;

public sealed class SetAiCapabilityAssignmentCommandHandler(
    IAiCapabilityAssignmentRepository assignments,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<SetAiCapabilityAssignmentCommandHandler> logger) : IRequestHandler<SetAiCapabilityAssignmentCommand>
{
    public async Task Handle(SetAiCapabilityAssignmentCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var existing = await assignments.GetByCapabilityAsync(request.Capability, cancellationToken);

        if (request.ProviderId is not { } providerId)
        {
            if (existing is not null)
            {
                assignments.Remove(existing);
            }
        }
        else if (existing is null)
        {
            assignments.Add(AiCapabilityAssignment.Create(request.Capability, providerId, actorUserId));
        }
        else
        {
            existing.AssignTo(providerId, actorUserId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = $"{request.Capability} -> {request.ProviderId?.ToString() ?? "platform default"}";
            AiAdminActionLog.AdminAiProviderActionPerformed(
                logger, "SetCapabilityAssignment", actorUserId, request.ProviderId ?? Guid.Empty, detail);
        }
    }
}
