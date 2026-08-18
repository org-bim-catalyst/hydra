using MediatR;

namespace AskLucy.Application.Prompts.Commands.SetPinned;

public sealed record SetPinnedCommand(Guid PromptId, bool IsPinned) : IRequest;
