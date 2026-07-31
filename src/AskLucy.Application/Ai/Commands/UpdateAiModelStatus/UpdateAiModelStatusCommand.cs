using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.UpdateAiModelStatus;

/// <summary>contracts/admin-ai-models.md `PATCH /api/v1/admin/ai/models/{id}` — any transition is allowed (FR-002).</summary>
public sealed record UpdateAiModelStatusCommand(Guid ModelId, AIModelStatus Status) : IRequest;
