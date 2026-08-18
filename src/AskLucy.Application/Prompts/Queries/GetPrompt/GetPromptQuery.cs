using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetPrompt;

/// <summary>Full detail view of one prompt the caller owns (spec.md FR-005, contracts/prompts-api.md).</summary>
public sealed record GetPromptQuery(Guid Id) : IRequest<PromptDetailDto>;
