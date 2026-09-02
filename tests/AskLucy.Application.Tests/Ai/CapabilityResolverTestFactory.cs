using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// Builds an <see cref="AiCapabilityProviderResolver"/> with no capability assignments, which
/// falls through to <see cref="DefaultProviderResolver"/>. That keeps every pre-existing test
/// asserting the behaviour it was written for: these suites are about what each service does
/// once a provider has been resolved, not about which provider an administrator picked.
/// </summary>
public static class CapabilityResolverTestFactory
{
    public static AiCapabilityProviderResolver Unassigned(IAIProviderRepository providers, IAIModelRepository models)
    {
        var assignments = Substitute.For<IAiCapabilityAssignmentRepository>();
        return new AiCapabilityProviderResolver(
            assignments,
            providers,
            models,
            new DefaultProviderResolver(providers, models),
            NullLogger<AiCapabilityProviderResolver>.Instance);
    }
}
