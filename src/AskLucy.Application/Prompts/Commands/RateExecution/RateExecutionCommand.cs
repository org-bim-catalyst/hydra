using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RateExecution;

/// <summary>Manual evaluation of a test execution's output (spec.md FR-044) — creates or updates the execution's single rating.</summary>
public sealed record RateExecutionCommand(Guid ExecutionId, PromptRatingValue RatingValue) : IRequest;
