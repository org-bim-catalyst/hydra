using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.SaveTestCase;

public sealed class SaveTestCaseCommandHandler(
    IPromptTestCaseRepository testCaseRepository,
    IPromptRepository promptRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<SaveTestCaseCommand, PromptTestCaseDto>
{
    public async Task<PromptTestCaseDto> Handle(SaveTestCaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var testCase = PromptTestCase.Create(
            request.PromptId, request.Name, request.VariableValuesJson, request.ExpectedOutput,
            request.EvaluationCriteria, request.ProviderKey, request.ModelKey, request.SourceExecutionId, userId);

        testCaseRepository.Add(testCase);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PromptTestCaseDto.FromEntity(testCase);
    }
}
