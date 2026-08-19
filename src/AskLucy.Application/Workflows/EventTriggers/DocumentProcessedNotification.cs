using MediatR;

namespace AskLucy.Application.Workflows.EventTriggers;

/// <summary>Published from the terminal (success) stage of <c>DocumentProcessingPipeline.RunJobAsync</c>, immediately after its own commit succeeds (research.md Decision 12).</summary>
public sealed record DocumentProcessedNotification(Guid DocumentId, string OwnerId, string FileName) : INotification;
