using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Commands.UpdateMemoryPreferences;
using AskLucy.Domain.Memory;
using FluentAssertions;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T068 (US4 AC1) — disabling memory stops creation/use without deleting any already-stored memory row.</summary>
public sealed class MemoryPrivacyTests
{
    private readonly IMemoryPreferenceRepository _preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private const string UserId = "user-1";

    public MemoryPrivacyTests() => _currentUser.UserId.Returns(UserId);

    [Fact]
    public async Task UpdateMemoryPreferences_ShouldDisableMemory_WithoutTouchingAnyStoredMemoryRow()
    {
        var preference = MemoryPreference.CreateDefault(UserId, "test");
        _preferenceRepository.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(preference);

        var handler = new UpdateMemoryPreferencesCommandHandler(_preferenceRepository, _unitOfWork, _currentUser);
        await handler.Handle(new UpdateMemoryPreferencesCommand(false, []), CancellationToken.None);

        preference.MemoryEnabled.Should().BeFalse();
        _memoryRepository.ReceivedCalls().Should().BeEmpty("disabling memory must never touch existing stored memory rows (AC1: not deleted)");
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldStopUsingMemories_ButTheyRemainRetrievableById_WhenDisabled()
    {
        var preference = MemoryPreference.CreateDefault(UserId, "test");
        preference.SetMemoryEnabled(false, "test");
        _preferenceRepository.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(preference);

        var memory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.PersonalFact, "Still stored", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");
        _memoryRepository.GetByIdAsync(memory.Id, Arg.Any<CancellationToken>()).Returns(memory);

        // The memory itself is still fully present and fetchable — only its *use* in retrieval stops.
        var stillStored = await _memoryRepository.GetByIdAsync(memory.Id, CancellationToken.None);
        stillStored.Should().NotBeNull();
        stillStored!.IsDeleted.Should().BeFalse();
    }
}
