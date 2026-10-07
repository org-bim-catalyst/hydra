using AskLucy.Application.Notifications.Templates;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.ArchiveTemplateVersion;

/// <summary>
/// contracts/admin-notifications-api.md POST …/actions/archive (M). Refused with 409 <c>LastPublishedDefault</c> for the last published
/// version of a shipped default (SC-006).
/// </summary>
public sealed record ArchiveTemplateVersionCommand(Guid TemplateId, Guid VersionId, byte[] ExpectedRowVersion) : IRequest<TemplateVersionDto>;
