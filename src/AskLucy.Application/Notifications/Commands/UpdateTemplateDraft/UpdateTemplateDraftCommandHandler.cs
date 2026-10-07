using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Common;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.UpdateTemplateDraft;

public sealed class UpdateTemplateDraftCommandHandler(
    INotificationTemplateRepository templates,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateTemplateDraftCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> Handle(UpdateTemplateDraftCommand request, CancellationToken cancellationToken)
    {
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: true, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");
        var version = NotificationTemplateMapper.FindVersion(template, request.VersionId);

        NotificationTemplateMapper.EnsureDraft(version);
        NotificationTemplateMapper.EnsureCurrent(version, request.ExpectedRowVersion);
        templates.ExpectRowVersion(version, request.ExpectedRowVersion);

        try
        {
            template.UpdateDraft(version.Id, request.Content);
        }
        catch (DomainRuleViolationException ex)
        {
            throw new NotificationTemplateRejectedException(ex.Message);
        }

        audit.Write(
            NotificationAuditAction.TemplateDraftSaved,
            "NotificationTemplateVersion",
            version.Id.ToString(),
            NotificationAuditOutcome.Succeeded,
            new { templateId = template.Id, type = template.Type, channel = template.Channel.ToString(), language = template.Language, versionNumber = version.VersionNumber, created = false });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NotificationTemplateMapper.ToDto(version);
    }
}
