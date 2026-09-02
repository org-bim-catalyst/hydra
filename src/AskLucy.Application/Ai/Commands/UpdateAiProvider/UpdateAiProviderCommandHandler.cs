using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.UpdateAiProvider;

public sealed class UpdateAiProviderCommandHandler(
    IAIProviderRepository providers,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<UpdateAiProviderCommandHandler> logger) : IRequestHandler<UpdateAiProviderCommand>
{
    public async Task Handle(UpdateAiProviderCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var provider = await providers.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");

        var wasEnabled = provider.IsEnabled;

        if (request.IsEnabled is { } isEnabled)
        {
            if (isEnabled)
            {
                provider.Enable(actorUserId);
            }
            else
            {
                provider.Disable(actorUserId);
            }
        }

        // Only touch DefaultModelId when the caller actually supplied it — a PATCH that only
        // sets IsEnabled must not clear an existing DefaultModelId as a side effect.
        if (request.DefaultModelId.HasValue)
        {
            provider.SetDefaultModel(request.DefaultModelId, actorUserId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // CA1873 — the interpolated detail string is only built when Information logging is
        // actually enabled, and passed in already-computed (as a plain local) rather than as a
        // formatted expression at the call site.
        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = $"isEnabled: {wasEnabled} -> {provider.IsEnabled}";
            AiAdminActionLog.AdminAiProviderActionPerformed(logger, "UpdateProvider", actorUserId, provider.Id, detail);
        }
    }
}
