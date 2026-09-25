using AskLucy.Domain.Ai;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.OperationalFailures;

public sealed class OperationalFailureKindsTests
{
    [Fact]
    public void FromProvider_MapsEveryProviderKindToTheSameName()
    {
        foreach (var kind in Enum.GetValues<AiProviderFailureKind>())
        {
            var mapped = OperationalFailureKinds.FromProvider(kind);

            mapped.ToString().Should().Be(kind.ToString());
            OperationalFailureKinds.IsProviderKind(mapped).Should().BeTrue();
        }
    }

    [Fact]
    public void IsProviderKind_IsFalseForNonProviderKinds()
    {
        var providerNames = Enum.GetNames<AiProviderFailureKind>().ToHashSet();

        foreach (var kind in Enum.GetValues<OperationalFailureKind>().Where(k => !providerNames.Contains(k.ToString())))
        {
            OperationalFailureKinds.IsProviderKind(kind).Should().BeFalse(kind.ToString());
        }
    }
}
