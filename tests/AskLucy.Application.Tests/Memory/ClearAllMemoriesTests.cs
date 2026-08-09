using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Commands.ClearAllMemories;
using AskLucy.Domain.Memory;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T069 (US4 AC2, FR-023) — clear-all only proceeds with explicit confirmation, and immediately soft-deletes every one of the caller's own memories.</summary>
public sealed class ClearAllMemoriesTests
{
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IMemoryAuditLogRepository _auditLogRepository = Substitute.For<IMemoryAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private const string UserId = "user-1";

    public ClearAllMemoriesTests() => _currentUser.UserId.Returns(UserId);

    [Fact]
    public async Task Validator_ShouldReject_WhenConfirmIsFalse()
    {
        var validator = new ClearAllMemoriesCommandValidator();

        var result = await validator.ValidateAsync(new ClearAllMemoriesCommand(false));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldSoftDeleteEveryMemory_WhenConfirmed()
    {
        var memories = Enumerable.Range(0, 3)
            .Select(i => MemoryEntity.CreateCandidate(
                UserId, null, MemoryCategory.PersonalFact, $"Fact {i}", MemorySourceType.PassiveConversationAnalysis,
                null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test"))
            .ToList();
        _memoryRepository.GetAllByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(memories);

        var handler = new ClearAllMemoriesCommandHandler(_memoryRepository, _auditLogRepository, _unitOfWork, _currentUser);
        await handler.Handle(new ClearAllMemoriesCommand(true), CancellationToken.None);

        memories.Should().OnlyContain(m => m.IsDeleted);
        _auditLogRepository.Received(3).Add(Arg.Is<MemoryAuditLog>(a => a.Action == MemoryAuditAction.Deleted));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
