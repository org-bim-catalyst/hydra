using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Authorization;

public sealed class SuperUserSafeguardTests
{
    [Fact]
    public void EnsureAtLeastOneActiveSuperUserRemains_WhenZeroAfterRemoval_ShouldThrow()
    {
        // Removing the last Super User.
        var act = () => SuperUserSafeguard.EnsureAtLeastOneActiveSuperUserRemains(1, 1);
        act.Should().Throw<DomainRuleViolationException>()
           .WithMessage("*at least one active Super User*");
    }

    [Fact]
    public void EnsureAtLeastOneActiveSuperUserRemains_WhenOneRemainsAfterRemoval_ShouldNotThrow()
    {
        // Two Super Users, removing one — should succeed.
        var act = () => SuperUserSafeguard.EnsureAtLeastOneActiveSuperUserRemains(2, 1);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void EnsureAtLeastOneActiveSuperUserRemains_WhenRemovingZero_ShouldNeverThrow(int activeBefore)
    {
        // No removals — must always be safe regardless of current count.
        var act = () => SuperUserSafeguard.EnsureAtLeastOneActiveSuperUserRemains(activeBefore, 0);
        act.Should().NotThrow();
    }
}
