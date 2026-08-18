using AskLucy.Domain.Memory;
using FluentAssertions;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>
/// tasks.md T058 (FR-008, SC-004) — proves the enforcement mechanism: a candidate flagged
/// sensitive always lands <see cref="MemoryLifecycleState.PendingApproval"/> regardless of the
/// category's configured <see cref="MemoryApprovalMode"/>, across every mode/isSensitive
/// combination (a deterministic 100% here, since <see cref="MemoryEntity.CreateCandidate"/>'s rule
/// is unconditional). SC-004's "&#8805;95% correctly flagged across a labeled test batch" is a
/// claim about the LLM extraction classification's own accuracy (research.md Decision 6/7) — that
/// is inherently non-deterministic and requires a real model call to measure, so it is out of
/// scope for this deterministic unit suite; this test instead proves the domain-level guarantee
/// that once something IS flagged sensitive (however that happened), it can never silently skip
/// review.
/// </summary>
public sealed class SensitiveContentTests
{
    [Theory]
    [InlineData(MemoryApprovalMode.Automatic)]
    [InlineData(MemoryApprovalMode.Manual)]
    public void CreateCandidate_ShouldAlwaysRequireApproval_WhenFlaggedSensitive_RegardlessOfConfiguredMode(MemoryApprovalMode approvalMode)
    {
        var memory = MemoryEntity.CreateCandidate(
            "user-1", null, MemoryCategory.PersonalFact, "Has a chronic health condition", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.9m, isSensitive: true, approvalMode, "test");

        memory.State.Should().Be(MemoryLifecycleState.PendingApproval);
        memory.IsSensitive.Should().BeTrue();
    }

    [Fact]
    public void CreateCandidate_ShouldActivateImmediately_WhenAutomaticModeAndNotSensitive()
    {
        var memory = MemoryEntity.CreateCandidate(
            "user-1", null, MemoryCategory.UserPreference, "Prefers React", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.9m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

        memory.State.Should().Be(MemoryLifecycleState.Active);
    }

    [Fact]
    public void CreateCandidate_ShouldThrow_WhenCategoryModeIsDisabled_RegardlessOfSensitivity()
    {
        var act = () => MemoryEntity.CreateCandidate(
            "user-1", null, MemoryCategory.PersonalFact, "Anything", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.9m, isSensitive: true, MemoryApprovalMode.Disabled, "test");

        act.Should().Throw<AskLucy.Domain.Common.DomainRuleViolationException>();
    }
}
