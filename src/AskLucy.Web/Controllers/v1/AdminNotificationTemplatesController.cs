using AskLucy.Application.Notifications.Commands.ArchiveTemplateVersion;
using AskLucy.Application.Notifications.Commands.CreateTemplateDraft;
using AskLucy.Application.Notifications.Commands.PublishTemplateVersion;
using AskLucy.Application.Notifications.Commands.SendTemplateTest;
using AskLucy.Application.Notifications.Commands.UpdateTemplateDraft;
using AskLucy.Application.Notifications.Queries.GetNotificationTemplate;
using AskLucy.Application.Notifications.Queries.GetNotificationTemplates;
using AskLucy.Application.Notifications.Queries.GetNotificationTemplateVersion;
using AskLucy.Application.Notifications.Queries.PreviewTemplateVersion;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Administrators' template editor (specs/067 US7, contracts/admin-notifications-api.md): versioned drafts, preview, a test send to
/// the caller, publish and archive. Reading needs <c>admin.notifications.view</c>, changing needs <c>admin.notifications.manage</c>.
/// Edits are guarded by <c>If-Match</c>, the base64 row version of the version as last read.
/// </summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/notifications/templates")]
public sealed class AdminNotificationTemplatesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<IReadOnlyList<NotificationTemplateSummaryDto>>> List(
        [FromQuery] NotificationCategory? category,
        [FromQuery] NotificationChannel? channel,
        [FromQuery] string? language,
        [FromQuery] string? type,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationTemplatesQuery(category, channel, language, type), cancellationToken));

    [HttpGet("{templateId:guid}")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<NotificationTemplateDetailDto>> Get(Guid templateId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationTemplateQuery(templateId), cancellationToken));

    [HttpGet("{templateId:guid}/versions/{versionId:guid}")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<TemplateVersionDto>> GetVersion(Guid templateId, Guid versionId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationTemplateVersionQuery(templateId, versionId), cancellationToken));

    [HttpPost("{templateId:guid}/versions")]
    [RequirePermission("admin.notifications.manage")]
    public async Task<ActionResult<TemplateVersionDto>> CreateDraft(
        Guid templateId, [FromBody] TemplateVersionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CreateTemplateDraftCommand(templateId, ToContent(request), request.CopyFromVersionId), cancellationToken);
        return Created($"/api/v1/admin/notifications/templates/{templateId}/versions/{result.Id}", result);
    }

    [HttpPut("{templateId:guid}/versions/{versionId:guid}")]
    [RequirePermission("admin.notifications.manage")]
    public async Task<ActionResult<TemplateVersionDto>> UpdateDraft(
        Guid templateId, Guid versionId, [FromBody] TemplateVersionRequest request, CancellationToken cancellationToken)
    {
        if (!TryReadIfMatch(out var rowVersion))
        {
            return IfMatchRequired();
        }

        return Ok(await mediator.Send(
            new UpdateTemplateDraftCommand(templateId, versionId, ToContent(request) ?? new NotificationTemplateContent(), rowVersion), cancellationToken));
    }

    [HttpPost("{templateId:guid}/versions/{versionId:guid}/actions/preview")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<TemplatePreviewDto>> Preview(
        Guid templateId, Guid versionId, [FromBody] PreviewTemplateRequest? request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PreviewTemplateVersionQuery(templateId, versionId, request?.Variables), cancellationToken));

    [HttpPost("{templateId:guid}/versions/{versionId:guid}/actions/send-test")]
    [RequirePermission("admin.notifications.manage")]
    [EnableRateLimiting("notifications-test-send")]
    public async Task<IActionResult> SendTest(Guid templateId, Guid versionId, CancellationToken cancellationToken) =>
        Accepted(await mediator.Send(new SendTemplateTestCommand(templateId, versionId), cancellationToken));

    [HttpPost("{templateId:guid}/versions/{versionId:guid}/actions/publish")]
    [RequirePermission("admin.notifications.manage")]
    public async Task<ActionResult<TemplateVersionDto>> Publish(Guid templateId, Guid versionId, CancellationToken cancellationToken) =>
        TryReadIfMatch(out var rowVersion)
            ? Ok(await mediator.Send(new PublishTemplateVersionCommand(templateId, versionId, rowVersion), cancellationToken))
            : IfMatchRequired();

    [HttpPost("{templateId:guid}/versions/{versionId:guid}/actions/archive")]
    [RequirePermission("admin.notifications.manage")]
    public async Task<ActionResult<TemplateVersionDto>> Archive(Guid templateId, Guid versionId, CancellationToken cancellationToken) =>
        TryReadIfMatch(out var rowVersion)
            ? Ok(await mediator.Send(new ArchiveTemplateVersionCommand(templateId, versionId, rowVersion), cancellationToken))
            : IfMatchRequired();

    private static NotificationTemplateContent? ToContent(TemplateVersionRequest request)
    {
        var empty = request.Subject is null && request.Preheader is null && request.Greeting is null && request.Heading is null
            && request.BodyParagraphs is null && request.ActionLabel is null && request.SafetyNote is null && request.FooterNote is null
            && request.Title is null && request.Message is null;
        return empty
            ? null
            : new NotificationTemplateContent
            {
                Subject = request.Subject,
                Preheader = request.Preheader,
                Greeting = request.Greeting,
                Heading = request.Heading,
                BodyParagraphs = request.BodyParagraphs ?? [],
                ActionLabel = request.ActionLabel,
                SafetyNote = request.SafetyNote,
                FooterNote = request.FooterNote,
                Title = request.Title,
                Message = request.Message,
            };
    }

    /// <summary>The base64 row version, as sent in the version's <c>rowVersion</c> field; quotes around an ETag-style value are tolerated.</summary>
    private bool TryReadIfMatch(out byte[] rowVersion)
    {
        rowVersion = [];
        var header = Request.Headers.IfMatch.ToString().Trim().Trim('"');
        if (header.Length == 0)
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(header);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private ObjectResult IfMatchRequired() =>
        Problem(
            statusCode: StatusCodes.Status428PreconditionRequired,
            title: "If-Match required",
            detail: "Send the version's rowVersion in the If-Match header.");
}
