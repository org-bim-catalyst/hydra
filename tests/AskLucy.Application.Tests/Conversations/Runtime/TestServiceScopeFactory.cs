using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 Phase 7 test support — <see cref="SubAgentDelegator"/> resolves a fresh
/// <c>IServiceScope</c> per delegated slice, which NSubstitute cannot fake meaningfully (there is
/// no behaviour to stub; what matters is that a real scope and provider come back). This wraps a
/// minimal, real <see cref="ServiceProvider"/> that registers exactly the
/// <see cref="ConversationCapabilityCatalog"/>/<see cref="CapabilityExecutor"/> instances the
/// calling test already built, so every "fresh scope" the delegator creates resolves back to the
/// same test doubles the test configured — appropriate here because these unit tests have no
/// <c>DbContext</c> to isolate, unlike the real per-request registration this stands in for.
/// </summary>
internal static class TestServiceScopeFactory
{
    public static IServiceScopeFactory Create(ConversationCapabilityCatalog capabilityCatalog, CapabilityExecutor capabilityExecutor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(capabilityCatalog);
        services.AddSingleton(capabilityExecutor);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
