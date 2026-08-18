using MediatR;

namespace AskLucy.Application.Prompts.Commands.AddTag;

public sealed record AddTagCommand(Guid PromptId, string Value) : IRequest<Guid>;
