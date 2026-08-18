using MediatR;

namespace AskLucy.Application.Prompts.Commands.DeleteTestCase;

public sealed record DeleteTestCaseCommand(Guid PromptId, Guid TestCaseId) : IRequest;
