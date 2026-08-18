using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetVersion;

public sealed record GetVersionQuery(Guid PromptId, int VersionNumber) : IRequest<PromptVersionDetailDto>;
