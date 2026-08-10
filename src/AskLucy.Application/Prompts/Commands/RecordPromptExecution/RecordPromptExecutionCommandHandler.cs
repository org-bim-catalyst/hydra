using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RecordPromptExecution;

public sealed class RecordPromptExecutionCommandHandler(
    IPromptRepository promptRepository,
    IPromptExecutionRepository executionRepository,
    IAIModelRepository modelRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RecordPromptExecutionCommand, Guid>
{
    public async Task<Guid> Handle(RecordPromptExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var execution = PromptExecution.CreatePending(
            request.PromptId, request.PromptVersionId, request.Origin, request.ProviderKey, request.ModelKey,
            request.Temperature, request.MaxOutputTokens, request.StructuredOutputRequested,
            request.ResolvedVariableValuesJson, request.RequestedRagContext, request.RequestedMemoryContext, userId);

        if (request.Outcome == PromptExecutionOutcome.Success)
        {
            execution.MarkSucceeded(request.LatencyMs, request.ResultMessageId);

            if (request.Origin == PromptExecutionOrigin.TestingWorkspace)
            {
                var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken);
                var estimatedCostUsd = CostEstimator.Estimate(model?.Pricing, request.InputTokenCount, request.OutputTokenCount);

                var result = PromptExecutionResult.Create(
                    execution.Id, request.OutputText ?? string.Empty, request.InputTokenCount, request.OutputTokenCount,
                    estimatedCostUsd, request.RagCitationsJson, request.MemoryReferencesJson, userId);
                executionRepository.AddResult(result);
            }

            var usageStatistics = await promptRepository.GetUsageStatisticsAsync(request.PromptId, cancellationToken);
            usageStatistics?.RecordSuccessfulUse();
        }
        else
        {
            execution.MarkFailed(request.ErrorDetail ?? "The prompt execution failed.", request.LatencyMs);
        }

        executionRepository.Add(execution);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return execution.Id;
    }
}
