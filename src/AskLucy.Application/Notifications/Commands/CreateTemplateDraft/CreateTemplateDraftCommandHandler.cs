using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Common;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.CreateTemplateDraft;

public sealed class CreateTemplateDraftCommandHandler(
    INotificationTemplateRepository templates,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<CreateTemplateDraftCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> Handle(CreateTemplateDraftCommand request, CancellationToken cancellationToken)
    {
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: true, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");

        var content = request.Content
            ?? NotificationTemplateMapper.FindVersion(template, request.CopyFromVersionId!.Value).Content;

        NotificationTemplateVersion version;
        try
        {
            version = template.AddDraft(content, timeProvider.GetUtcNow().UtcDateTime);
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
            new { templateId = template.Id, type = template.Type, channel = template.Channel.ToString(), language = template.Language, versionNumber = version.VersionNumber, created = true });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NotificationTemplateMapper.ToDto(version);
    }
}
