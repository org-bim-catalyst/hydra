using AskLucy.Application.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Dictation.Commands.SelectLocalWhisperModel;

/// <summary>
/// specs/078 FR-009a — selects the Custom Models deployment Local Whisper uses; null selects none,
/// so Local Whisper's paths use the browser built-in. Never changes the primary engine.
/// </summary>
public sealed record SelectLocalWhisperModelCommand(Guid? CustomModelId, string RowVersion) : IRequest;

public sealed class SelectLocalWhisperModelCommandValidator : AbstractValidator<SelectLocalWhisperModelCommand>
{
    public SelectLocalWhisperModelCommandValidator()
    {
        RuleFor(c => c.CustomModelId).NotEqual(Guid.Empty);
        RuleFor(c => c.RowVersion).Must(DictationSettingConcurrency.IsBase64).WithMessage("rowVersion is required.");
    }
}

public sealed class SelectLocalWhisperModelCommandHandler(
    IDictationEngineSettingRepository settings,
    ILocalWhisperModelCatalog catalog,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<SelectLocalWhisperModelCommandHandler> logger) : IRequestHandler<SelectLocalWhisperModelCommand>
{
    public async Task Handle(SelectLocalWhisperModelCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var setting = await settings.GetOrCreateAsync(cancellationToken);
        DictationSettingConcurrency.EnsureCurrent(setting, request.RowVersion);

        if (request.CustomModelId is { } id)
        {
            var option = await catalog.CheckSelectableAsync(id, cancellationToken);
            if (!option.Selectable)
            {
                throw new LocalWhisperModelNotSelectableException(option.Reason ?? "This deployment can't be the Local Whisper model.");
            }
        }

        setting.SelectLocalWhisperModel(request.CustomModelId, actorUserId, timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = request.CustomModelId?.ToString() ?? "none";
            DictationAdminLog.ActionPerformed(logger, "SelectLocalWhisperModel", actorUserId, detail);
        }
    }
}
