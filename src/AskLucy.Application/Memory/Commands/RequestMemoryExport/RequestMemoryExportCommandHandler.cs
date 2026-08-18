using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Hangfire;
using MediatR;

namespace AskLucy.Application.Memory.Commands.RequestMemoryExport;

public sealed class RequestMemoryExportCommandHandler(
    IMemoryExportJobRepository exportJobRepository, IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser, IBackgroundJobClient backgroundJobClient) : IRequestHandler<RequestMemoryExportCommand, Guid>
{
    public async Task<Guid> Handle(RequestMemoryExportCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var job = MemoryExportJob.CreateProcessing(userId, userId);
        exportJobRepository.Add(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<IMemoryExportGenerationJob>(j => j.RunAsync(job.Id, CancellationToken.None));

        return job.Id;
    }
}
