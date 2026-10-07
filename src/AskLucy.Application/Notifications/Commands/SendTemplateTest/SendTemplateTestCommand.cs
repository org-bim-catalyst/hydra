using AskLucy.Application.Notifications.Templates;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.SendTemplateTest;

/// <summary>
/// contracts/admin-notifications-api.md POST …/actions/send-test (M). Sends the rendered email version to the calling administrator's own
/// verified address and nowhere else: there is no recipient field.
/// </summary>
public sealed record SendTemplateTestCommand(Guid TemplateId, Guid VersionId) : IRequest<SendTemplateTestResult>;
