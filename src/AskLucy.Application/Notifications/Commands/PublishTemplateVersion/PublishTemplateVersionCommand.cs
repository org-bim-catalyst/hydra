using AskLucy.Application.Notifications.Templates;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.PublishTemplateVersion;

/// <summary>contracts/admin-notifications-api.md POST …/actions/publish (M). Archives the version it replaces (FR-039).</summary>
public sealed record PublishTemplateVersionCommand(Guid TemplateId, Guid VersionId, byte[] ExpectedRowVersion) : IRequest<TemplateVersionDto>;
