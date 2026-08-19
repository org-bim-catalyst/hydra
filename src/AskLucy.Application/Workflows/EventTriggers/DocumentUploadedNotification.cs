using MediatR;

namespace AskLucy.Application.Workflows.EventTriggers;

/// <summary>
/// Published (research.md Decision 12) immediately after <c>UploadDocumentCommandHandler</c>'s own
/// commit succeeds — the first real "domain event dispatched after a successful commit" instance in
/// this codebase (constitution §3). <see cref="WorkflowEventTriggerHandler"/> is the only subscriber.
/// </summary>
public sealed record DocumentUploadedNotification(Guid DocumentId, Guid KnowledgeBaseId, string OwnerId, string FileName) : INotification;
