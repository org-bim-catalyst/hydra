using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using AskLucy.Infrastructure.Memory;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Infrastructure.Tests.Memory;

/// <summary>
/// tasks.md T101a (FR-031, research.md Decision 18, added during `/speckit-analyze` remediation
/// finding C1) — <see cref="MemoryCleanupJob"/> soft-deletes exactly the candidates
/// <see cref="IMemoryRepository.GetCleanupCandidatesAsync"/> returns and writes an
/// <see cref="MemoryAuditAction.Expired"/> entry per removal; the "never
/// Active/PendingApproval" exclusion itself is enforced by that repository method's own SQL
/// filter (<c>MemoryRepository</c>, Persistence layer, real-SQL-Server-only per constitution §10)
/// — this test proves the job's own contract: process whatever it's handed, one failure never
/// blocks the rest of the batch.
/// </summary>
public sealed class MemoryCleanupJobTests
{
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IMemoryAuditLogRepository _auditLogRepository = Substitute.For<IMemoryAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly MemoryCleanupJob _job;

    public MemoryCleanupJobTests() =>
        _job = new MemoryCleanupJob(_memoryRepository, _auditLogRepository, _unitOfWork, Substitute.For<ILogger<MemoryCleanupJob>>());

    private static MemoryEntity CreateActive(string userId) =>
        MemoryEntity.CreateCandidate(
            userId, null, MemoryCategory.PersonalFact, "Some fact", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

    [Fact]
    public async Task RunAsync_ShouldSoftDeleteEveryCandidate_AndWriteAnExpiredAuditLogEntryForEach()
    {
        var candidates = new List<MemoryEntity> { CreateActive("user-1"), CreateActive("user-2") };
        _memoryRepository.GetCleanupCandidatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(candidates);

        await _job.RunAsync(CancellationToken.None);

        candidates.Should().OnlyContain(m => m.IsDeleted);
        _auditLogRepository.Received(2).Add(Arg.Is<MemoryAuditLog>(a => a.Action == MemoryAuditAction.Expired));
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldContinueTheBatch_WhenOneCandidateFailsToSave()
    {
        var candidates = new List<MemoryEntity> { CreateActive("user-1"), CreateActive("user-2") };
        _memoryRepository.GetCleanupCandidatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(candidates);

        var callCount = 0;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("Transient failure.");
            }

            return Task.FromResult(1);
        });

        await _job.RunAsync(CancellationToken.None);

        callCount.Should().Be(2, "the second candidate must still be attempted after the first one's save fails");
        candidates[1].IsDeleted.Should().BeTrue();
    }
}
