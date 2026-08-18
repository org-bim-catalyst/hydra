using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Commands.DeleteMemory;
using AskLucy.Application.Memory.Commands.EditMemory;
using AskLucy.Application.Memory.Queries.GetMemory;
using AskLucy.Domain.Memory;
using FluentAssertions;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T046 (FR-027) — a request naming a memory the caller doesn't own reports not-found, never confirming existence (no distinct 403/"forbidden" path), across every Memory Center operation.</summary>
public sealed class MemoryOwnershipTests
{
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly MemoryEntity _othersMemory;

    public MemoryOwnershipTests()
    {
        _othersMemory = MemoryEntity.CreateCandidate(
            "owner-1", null, MemoryCategory.PersonalFact, "Someone else's memory", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

        _currentUser.UserId.Returns("attacker-2");
        _memoryRepository.GetByIdAsync(_othersMemory.Id, Arg.Any<CancellationToken>()).Returns(_othersMemory);
    }

    [Fact]
    public async Task GetMemory_ShouldThrowKeyNotFound_WhenTheMemoryBelongsToAnotherUser()
    {
        var handler = new GetMemoryQueryHandler(
            _memoryRepository, Substitute.For<IMemoryVersionRepository>(), Substitute.For<IMemoryConflictRepository>(), _currentUser);

        var act = () => handler.Handle(new GetMemoryQuery(_othersMemory.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task EditMemory_ShouldThrowKeyNotFound_WhenTheMemoryBelongsToAnotherUser()
    {
        var handler = new EditMemoryCommandHandler(
            _memoryRepository, Substitute.For<IMemoryVersionRepository>(), Substitute.For<IMemoryAuditLogRepository>(),
            Substitute.For<IMemoryEmbeddingRepository>(), Substitute.For<IEmbeddingProviderRepository>(),
            Substitute.For<IEmbeddingServiceResolver>(), Substitute.For<IMemoryVectorStore>(), Substitute.For<IUnitOfWork>(), _currentUser);

        var act = () => handler.Handle(new EditMemoryCommand(_othersMemory.Id, "New content"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteMemory_ShouldThrowKeyNotFound_WhenTheMemoryBelongsToAnotherUser()
    {
        var handler = new DeleteMemoryCommandHandler(
            _memoryRepository, Substitute.For<IMemoryAuditLogRepository>(), Substitute.For<IUnitOfWork>(), _currentUser);

        var act = () => handler.Handle(new DeleteMemoryCommand(_othersMemory.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
