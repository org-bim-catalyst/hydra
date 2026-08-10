using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.ListTestCases;

public sealed class ListTestCasesQueryHandler(
    IPromptTestCaseRepository testCaseRepository,
    IPromptRepository promptRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListTestCasesQuery, IReadOnlyList<PromptTestCaseDto>>
{
    public async Task<IReadOnlyList<PromptTestCaseDto>> Handle(ListTestCasesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var testCases = await testCaseRepository.ListForPromptAsync(request.PromptId, cancellationToken);
        return [.. testCases.Select(PromptTestCaseDto.FromEntity)];
    }
}
