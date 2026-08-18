using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.DeleteTestCase;

public sealed class DeleteTestCaseCommandHandler(
    IPromptTestCaseRepository testCaseRepository,
    IPromptRepository promptRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteTestCaseCommand>
{
    public async Task Handle(DeleteTestCaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var testCase = await testCaseRepository.GetByIdAsync(request.TestCaseId, cancellationToken);
        if (testCase is null || testCase.PromptId != request.PromptId)
        {
            throw new KeyNotFoundException("Test case not found.");
        }

        testCase.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
