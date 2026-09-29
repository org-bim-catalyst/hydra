using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Dictation.Commands.SetPushToTalkEngine;

/// <summary>specs/078 FR-017 — sets the Push-to-Talk engine used under ElevenLabs realtime (PUT dictation/push-to-talk).</summary>
public sealed record SetPushToTalkEngineCommand(DictationClipEngine Engine, string RowVersion) : IRequest;

public sealed class SetPushToTalkEngineCommandValidator : AbstractValidator<SetPushToTalkEngineCommand>
{
    public SetPushToTalkEngineCommandValidator()
    {
        RuleFor(c => c.Engine).IsInEnum();
        RuleFor(c => c.RowVersion).Must(DictationSettingConcurrency.IsBase64).WithMessage("rowVersion is required.");
    }
}

public sealed class SetPushToTalkEngineCommandHandler(
    IDictationEngineSettingRepository settings,
    DictationVendorGate vendorGate,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<SetPushToTalkEngineCommandHandler> logger) : IRequestHandler<SetPushToTalkEngineCommand>
{
    public async Task Handle(SetPushToTalkEngineCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var setting = await settings.GetOrCreateAsync(cancellationToken);
        DictationSettingConcurrency.EnsureCurrent(setting, request.RowVersion);

        var turnEngine = DictationEngineSetting.ToTurnEngine(request.Engine);
        var problem = await vendorGate.WhyNotSelectableAsync(turnEngine, cancellationToken);
        if (problem is not null)
        {
            throw new DictationEngineNotSelectableException(problem);
        }

        setting.SetPushToTalkEngine(request.Engine, actorUserId, timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = request.Engine.ToString();
            DictationAdminLog.ActionPerformed(logger, "SetPushToTalkEngine", actorUserId, detail);
        }
    }
}
