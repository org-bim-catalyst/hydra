using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Flows;

/// <summary>
/// <see cref="ConversationFlowCatalog.PromoteLoneFirstStep"/>: a lone slice that is a flow's first
/// step runs the whole flow; nothing else changes.
/// </summary>
public sealed class ConversationFlowCatalogTests
{
    private readonly ConversationFlowCatalog _catalog = new([new LocateAPlaceFlow()]);
    private readonly TurnContext _context = new("user-1", Guid.NewGuid(), null, null, [], false, false, 0, new HashSet<AgentToolPermission>(), null);

    private static TurnSlice Slice(string key, string args = """{"query":"Dubai Mall"}""", int? dependsOn = null) =>
        new(key, args, null, dependsOn);

    [Fact]
    public void PromoteLoneFirstStep_ShouldRunTheWholeFlow_ForALoneResolveLocationSlice()
    {
        var decision = new TurnDecision(TurnIntent.Act, [Slice(ResolveLocationCapability.CapabilityKey)]);

        var promoted = _catalog.PromoteLoneFirstStep(decision, _context);

        promoted.IsFlowRun.Should().BeTrue();
        promoted.FlowKey.Should().Be(LocateAPlaceFlow.FlowKey);
        promoted.FlowArgumentsJson.Should().Be("""{"query":"Dubai Mall"}""");
        promoted.ThroughStepIndex.Should().BeNull("the whole flow runs, boundary included");
        promoted.Slices.Should().BeEmpty();
    }

    [Fact]
    public void PromoteLoneFirstStep_ShouldLeaveSlicesAlone_WhenTheLocationFeedsAnotherCapability()
    {
        var decision = new TurnDecision(TurnIntent.Act,
            [Slice(ResolveLocationCapability.CapabilityKey), Slice("get_weather", "{}", dependsOn: 0)]);

        _catalog.PromoteLoneFirstStep(decision, _context).Should().BeSameAs(decision);
    }

    [Fact]
    public void PromoteLoneFirstStep_ShouldLeaveAScopedFlowDecisionAlone()
    {
        var justFindIt = new TurnDecision(TurnIntent.Act, [], LocateAPlaceFlow.FlowKey, """{"query":"Dubai Mall"}""", ThroughStepIndex: 0);

        _catalog.PromoteLoneFirstStep(justFindIt, _context).Should().BeSameAs(justFindIt);
    }

    [Fact]
    public void PromoteLoneFirstStep_ShouldLeaveOtherLoneSlicesAlone()
    {
        var decision = new TurnDecision(TurnIntent.Act, [Slice(ResolveSiteBoundaryCapability.CapabilityKey)]);

        _catalog.PromoteLoneFirstStep(decision, _context).Should().BeSameAs(decision);
    }

    [Fact]
    public void PromoteLoneFirstStep_ShouldNeverTurnASuggestionIntoARun()
    {
        var decision = new TurnDecision(TurnIntent.Suggest, [], LocateAPlaceFlow.FlowKey, """{"query":"Dubai Mall"}""");

        _catalog.PromoteLoneFirstStep(decision, _context).Should().BeSameAs(decision);
    }
}
