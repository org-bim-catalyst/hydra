using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using MediatR;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Memory.Commands.CreateMemoryCandidate;

public sealed class CreateMemoryCandidateCommandHandler(
    IMemoryRepository memoryRepository,
    IMemoryPreferenceRepository preferenceRepository,
    IMemoryApprovalRepository approvalRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateMemoryCandidateCommand, Guid?>
{
    private const string SystemActor = "system:agent-runtime";

    public async Task<Guid?> Handle(CreateMemoryCandidateCommand request, CancellationToken cancellationToken)
    {
        var categoryPreference = await preferenceRepository.GetCategoryPreferenceAsync(request.UserId, request.Category, cancellationToken);
        if (categoryPreference is null)
        {
            categoryPreference = MemoryCategoryPreference.CreateDefault(request.UserId, request.Category, SystemActor);
            preferenceRepository.AddCategoryPreference(categoryPreference);
        }

        if (categoryPreference.ApprovalMode == MemoryApprovalMode.Disabled || !categoryPreference.IsEnabled)
        {
            return null;
        }

        var memory = MemoryEntity.CreateCandidate(
            request.UserId, request.ProjectId, request.Category, request.Content,
            MemorySourceType.AgentProposed, sourceConversationId: null,
            request.Importance, request.Confidence, request.IsSensitive, categoryPreference.ApprovalMode, SystemActor);
        memoryRepository.Add(memory);

        approvalRepository.Add(memory.State == MemoryLifecycleState.PendingApproval
            ? MemoryApproval.CreatePending(memory.Id, SystemActor)
            : MemoryApproval.CreateDecided(memory.Id, MemoryApprovalDecision.Approved, SystemActor));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return memory.Id;
    }
}
