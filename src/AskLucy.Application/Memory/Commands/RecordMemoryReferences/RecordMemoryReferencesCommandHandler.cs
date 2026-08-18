using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.RecordMemoryReferences;

public sealed class RecordMemoryReferencesCommandHandler(
    IMemoryReferenceRepository referenceRepository, IUnitOfWork unitOfWork) : IRequestHandler<RecordMemoryReferencesCommand>
{
    private const string SystemActor = "system:memory-reference";

    public async Task Handle(RecordMemoryReferencesCommand request, CancellationToken cancellationToken)
    {
        if (request.UsedMemories.Count == 0)
        {
            return;
        }

        referenceRepository.AddRange(request.UsedMemories.Select(m =>
            MemoryReference.Create(request.MessageId, m.MemoryId, m.RelevanceScore, m.Content, SystemActor)));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
