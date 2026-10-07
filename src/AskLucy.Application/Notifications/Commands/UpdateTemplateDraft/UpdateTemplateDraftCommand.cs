using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.UpdateTemplateDraft;

/// <summary>contracts/admin-notifications-api.md PUT …/versions/{versionId} (M): a draft only, guarded by the row version the caller last read.</summary>
public sealed record UpdateTemplateDraftCommand(Guid TemplateId, Guid VersionId, NotificationTemplateContent Content, byte[] ExpectedRowVersion)
    : IRequest<TemplateVersionDto>;
