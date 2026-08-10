using MediatR;

namespace AskLucy.Application.Prompts.Commands.RemoveTag;

public sealed record RemoveTagCommand(Guid PromptId, Guid TagId) : IRequest;
