using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListTestCases;

public sealed record ListTestCasesQuery(Guid PromptId) : IRequest<IReadOnlyList<PromptTestCaseDto>>;
