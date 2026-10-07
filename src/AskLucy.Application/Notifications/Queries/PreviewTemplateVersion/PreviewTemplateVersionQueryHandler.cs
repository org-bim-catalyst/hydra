using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.PreviewTemplateVersion;

public sealed class PreviewTemplateVersionQueryHandler(
    INotificationTemplateRepository templates,
    INotificationTemplatePreviewRenderer renderer) : IRequestHandler<PreviewTemplateVersionQuery, TemplatePreviewDto>
{
    public async Task<TemplatePreviewDto> Handle(PreviewTemplateVersionQuery request, CancellationToken cancellationToken)
    {
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: false, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");
        var version = NotificationTemplateMapper.FindVersion(template, request.VersionId);
        var variables = TemplateSamples.Build(template.Definition, request.Variables);
        var direction = renderer.DirectionOf(template.Language);

        try
        {
            if (template.Channel == NotificationChannel.Email)
            {
                var email = renderer.PreviewEmail(template.Definition, template.Language, version, variables);
                return new TemplatePreviewDto(email.Subject, email.HtmlBody, email.TextBody, null, null, null, template.Language, direction);
            }

            var inApp = renderer.PreviewInApp(template.Definition, template.Language, version, variables);
            return new TemplatePreviewDto(null, null, null, inApp.Title, inApp.Message, inApp.ActionLabel, template.Language, direction);
        }
        catch (NotificationRenderException ex)
        {
            throw new NotificationTemplateRejectedException(ex.Message);
        }
    }
}
