using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Dictation.Commands.SetDictationPrimaryEngine;

/// <summary>specs/078 FR-004 — sets the primary dictation engine (PUT dictation/primary).</summary>
public sealed record SetDictationPrimaryEngineCommand(DictationPrimaryEngine Engine, string RowVersion) : IRequest;

public sealed class SetDictationPrimaryEngineCommandValidator : AbstractValidator<SetDictationPrimaryEngineCommand>
{
    public SetDictationPrimaryEngineCommandValidator()
    {
        RuleFor(c => c.Engine).IsInEnum();
        RuleFor(c => c.RowVersion).Must(DictationSettingConcurrency.IsBase64).WithMessage("rowVersion is required.");
    }
}

public sealed class SetDictationPrimaryEngineCommandHandler(
    IDictationEngineSettingRepository settings,
    DictationVendorGate vendorGate,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<SetDictationPrimaryEngineCommandHandler> logger) : IRequestHandler<SetDictationPrimaryEngineCommand>
{
    public async Task Handle(SetDictationPrimaryEngineCommand request, CancellationToken cancellationToken)
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

        setting.SetPrimary(request.Engine, actorUserId, timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = request.Engine.ToString();
            DictationAdminLog.ActionPerformed(logger, "SetDictationPrimaryEngine", actorUserId, detail);
        }
    }
}
