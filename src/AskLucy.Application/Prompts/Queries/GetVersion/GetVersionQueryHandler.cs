using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetVersion;

public sealed class GetVersionQueryHandler(IPromptRepository promptRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetVersionQuery, PromptVersionDetailDto>
{
    public async Task<PromptVersionDetailDto> Handle(GetVersionQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var version = await promptRepository.GetVersionAsync(request.PromptId, request.VersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");

        return PromptVersionDetailDto.FromEntity(version);
    }
}
