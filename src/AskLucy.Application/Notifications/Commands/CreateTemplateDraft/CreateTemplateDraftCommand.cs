using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.CreateTemplateDraft;

/// <summary>
/// contracts/admin-notifications-api.md POST /notifications/templates/{templateId}/versions (M). The content is the channel's
/// fields; when it is null the draft starts as a copy of <paramref name="CopyFromVersionId"/>.
/// </summary>
public sealed record CreateTemplateDraftCommand(Guid TemplateId, NotificationTemplateContent? Content, Guid? CopyFromVersionId = null)
    : IRequest<TemplateVersionDto>;
