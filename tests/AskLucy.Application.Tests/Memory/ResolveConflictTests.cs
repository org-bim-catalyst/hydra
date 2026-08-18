using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Commands.ResolveMemoryConflict;
using AskLucy.Domain.Memory;
using FluentAssertions;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T093 (US6 AC3) — resolving a conflict updates `ResolutionStatus` and each side's retrieval eligibility; the losing side of a one-sided resolution is discarded, `KeepBoth` discards neither; a memory with no open conflict is rejected with `409` (`MemoryConflictNotPendingException`).</summary>
public sealed class ResolveConflictTests
{
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IMemoryConflictRepository _conflictRepository = Substitute.For<IMemoryConflictRepository>();
    private readonly IMemoryAuditLogRepository _auditLogRepository = Substitute.For<IMemoryAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ResolveMemoryConflictCommandHandler _handler;
    private const string UserId = "user-1";

    public ResolveConflictTests()
    {
        _currentUser.UserId.Returns(UserId);
        _handler = new ResolveMemoryConflictCommandHandler(_memoryRepository, _conflictRepository, _auditLogRepository, _unitOfWork, _currentUser);
    }

    private static MemoryEntity CreateActive(string content) =>
        MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.PersonalFact, content, MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

    [Fact]
    public async Task Handle_KeepExisting_ShouldDiscardTheNewCandidate_AndResolveTheConflict()
    {
        var existingMemory = CreateActive("Existing");
        var newMemory = CreateActive("New");
        var conflict = MemoryConflict.CreatePendingConfirmation(existingMemory.Id, newMemory.Id, "test");
        _memoryRepository.GetByIdAsync(existingMemory.Id, Arg.Any<CancellationToken>()).Returns(existingMemory);
        _memoryRepository.GetByIdAsync(newMemory.Id, Arg.Any<CancellationToken>()).Returns(newMemory);
        _conflictRepository.GetOpenByMemoryIdAsync(existingMemory.Id, Arg.Any<CancellationToken>()).Returns(conflict);

        await _handler.Handle(new ResolveMemoryConflictCommand(existingMemory.Id, MemoryConflictResolution.KeepExisting), CancellationToken.None);

        conflict.ResolutionStatus.Should().Be(MemoryConflictResolutionStatus.ResolvedKeepExisting);
        newMemory.IsDeleted.Should().BeTrue();
        existingMemory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_KeepNew_ShouldDiscardTheExistingMemory_AndResolveTheConflict()
    {
        var existingMemory = CreateActive("Existing");
        var newMemory = CreateActive("New");
        var conflict = MemoryConflict.CreatePendingConfirmation(existingMemory.Id, newMemory.Id, "test");
        _memoryRepository.GetByIdAsync(existingMemory.Id, Arg.Any<CancellationToken>()).Returns(existingMemory);
        _memoryRepository.GetByIdAsync(newMemory.Id, Arg.Any<CancellationToken>()).Returns(newMemory);
        _conflictRepository.GetOpenByMemoryIdAsync(newMemory.Id, Arg.Any<CancellationToken>()).Returns(conflict);

        await _handler.Handle(new ResolveMemoryConflictCommand(newMemory.Id, MemoryConflictResolution.KeepNew), CancellationToken.None);

        conflict.ResolutionStatus.Should().Be(MemoryConflictResolutionStatus.ResolvedKeepNew);
        existingMemory.IsDeleted.Should().BeTrue();
        newMemory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_KeepBoth_ShouldDiscardNeitherMemory_AndResolveTheConflict()
    {
        var existingMemory = CreateActive("Existing");
        var newMemory = CreateActive("New");
        var conflict = MemoryConflict.CreatePendingConfirmation(existingMemory.Id, newMemory.Id, "test");
        _memoryRepository.GetByIdAsync(existingMemory.Id, Arg.Any<CancellationToken>()).Returns(existingMemory);
        _conflictRepository.GetOpenByMemoryIdAsync(existingMemory.Id, Arg.Any<CancellationToken>()).Returns(conflict);

        await _handler.Handle(new ResolveMemoryConflictCommand(existingMemory.Id, MemoryConflictResolution.KeepBoth), CancellationToken.None);

        conflict.ResolutionStatus.Should().Be(MemoryConflictResolutionStatus.ResolvedKeepBoth);
        existingMemory.IsDeleted.Should().BeFalse();
        newMemory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldThrowConflict_WhenTheMemoryHasNoOpenConflict()
    {
        var memory = CreateActive("No conflict here");
        _memoryRepository.GetByIdAsync(memory.Id, Arg.Any<CancellationToken>()).Returns(memory);
        _conflictRepository.GetOpenByMemoryIdAsync(memory.Id, Arg.Any<CancellationToken>()).Returns((MemoryConflict?)null);

        var act = () => _handler.Handle(new ResolveMemoryConflictCommand(memory.Id, MemoryConflictResolution.KeepBoth), CancellationToken.None);

        await act.Should().ThrowAsync<MemoryConflictNotPendingException>();
    }
}
