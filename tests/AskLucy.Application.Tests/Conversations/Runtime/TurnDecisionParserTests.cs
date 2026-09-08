using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Options;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 T039 — reading the decide step's response.
///
/// <para>
/// Kept free of any model call on purpose: parsing is the part most likely to need adjusting as
/// prompts evolve, so a regression should be reproducible from a captured string rather than by
/// provoking a live provider. Every input below is a shape a real model actually produces —
/// fenced JSON, a preamble, an invented capability, a self-referential dependency.
/// </para>
/// </summary>
public sealed class TurnDecisionParserTests
{
    private static readonly IReadOnlySet<string> Available =
        new HashSet<string>(StringComparer.Ordinal) { "resolve_location", "search_knowledge_base" };

    private static TurnDecisionParser Parser(int maxSlices = 3) =>
        new(Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions { MaxCapabilityInvocationsPerTurn = maxSlices }));

    [Fact]
    public void Parse_ShouldReadAnActDecision_FromCleanJson()
    {
        var json = """{"intent":"act","slices":[{"capabilityKey":"resolve_location","arguments":{"query":"Al Safa Park 2"},"pendingLabel":"Looking for Al Safa Park 2","dependsOn":null}]}""";

        var result = Parser().Parse(json, Available);

        result.Succeeded.Should().BeTrue();
        result.Decision.Intent.Should().Be(TurnIntent.Act);
        result.Decision.Slices.Should().ContainSingle();
        result.Decision.Slices[0].CapabilityKey.Should().Be("resolve_location");
        result.Decision.Slices[0].PendingLabel.Should().Be("Looking for Al Safa Park 2");
        result.Decision.IsFastPath.Should().BeFalse();
    }

    [Theory]
    [InlineData("```json\n{\"intent\":\"answer\",\"slices\":[]}\n```")]
    [InlineData("Sure! Here is the decision:\n{\"intent\":\"answer\",\"slices\":[]}")]
    [InlineData("{\"intent\":\"answer\",\"slices\":[]}\nLet me know if you need anything else.")]
    public void Parse_ShouldTolerateFencesAndPreamble(string content)
    {
        // Routine model behaviour, not a fault. Treating it as a parse failure would burn the
        // corrective retry on something the parser can simply handle.
        var result = Parser().Parse(content, Available);

        result.Succeeded.Should().BeTrue();
        result.Decision.Intent.Should().Be(TurnIntent.Answer);
    }

    [Theory]
    [InlineData("answer", TurnIntent.Answer)]
    [InlineData("act", TurnIntent.Act)]
    [InlineData("suggest", TurnIntent.Suggest)]
    [InlineData("  ACT  ", TurnIntent.Act)]
    public void Parse_ShouldAcceptAllThreeIntents_CaseAndWhitespaceInsensitively(string intent, TurnIntent expected)
    {
        var json = $$$"""{"intent":"{{{intent}}}","slices":[{"capabilityKey":"resolve_location","arguments":{}}]}""";

        Parser().Parse(json, Available).Decision.Intent.Should().Be(expected);
    }

    [Fact]
    public void Parse_ShouldDiscardSlices_WhenIntentIsSuggest()
    {
        // "suggest" means answer in words and offer; a model that also supplies slices is
        // contradicting itself, and the intent wins because that is the field the prompt's
        // guidance is written against.
        var json = """{"intent":"suggest","slices":[{"capabilityKey":"resolve_location","arguments":{}}]}""";

        var result = Parser().Parse(json, Available);

        result.Decision.Intent.Should().Be(TurnIntent.Suggest);
        result.Decision.Slices.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldDropAnUnavailableCapability_AndSayWhy()
    {
        // The "360° images" failure, in its real form: a plausible key that does not exist.
        var json = """{"intent":"act","slices":[{"capabilityKey":"show_360_photos","arguments":{}},{"capabilityKey":"resolve_location","arguments":{"query":"X"}}]}""";

        var result = Parser().Parse(json, Available);

        result.Decision.Slices.Should().ContainSingle().Which.CapabilityKey.Should().Be("resolve_location");
        result.DroppedSliceReasons.Should().ContainSingle()
            .Which.Should().Contain("show_360_photos").And.Contain("not an available capability");
    }

    [Fact]
    public void Parse_ShouldDegradeToAnswer_WhenEveryProposedSliceIsDropped()
    {
        // An "act" turn with nothing left to do would show the user an acknowledgement for work
        // that never happens. Answering plainly is the honest outcome.
        var json = """{"intent":"act","slices":[{"capabilityKey":"show_360_photos","arguments":{}}]}""";

        var result = Parser().Parse(json, Available);

        result.Succeeded.Should().BeTrue();
        result.Decision.Intent.Should().Be(TurnIntent.Answer);
        result.Decision.IsFastPath.Should().BeTrue();
        result.DroppedSliceReasons.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_ShouldEnforceThePerTurnCap()
    {
        var json = """{"intent":"act","slices":[{"capabilityKey":"resolve_location","arguments":{}},{"capabilityKey":"resolve_location","arguments":{}},{"capabilityKey":"resolve_location","arguments":{}}]}""";

        var result = Parser(maxSlices: 2).Parse(json, Available);

        result.Decision.Slices.Should().HaveCount(2);
        result.DroppedSliceReasons.Should().ContainSingle().Which.Should().Contain("cap of 2");
    }

    [Theory]
    [InlineData(0)]   // self-reference on the first slice
    [InlineData(5)]   // forward reference
    [InlineData(-1)]  // nonsense
    public void Parse_ShouldRejectADependencyThatIsNotAnEarlierSlice(int dependsOn)
    {
        // A forward or self reference would deadlock the runner waiting on a result that can
        // never arrive, so it is dropped to null and the slice simply runs independently.
        var json = $$$"""{"intent":"act","slices":[{"capabilityKey":"resolve_location","arguments":{},"dependsOn":{{{dependsOn}}}}]}""";

        var result = Parser().Parse(json, Available);

        result.Decision.Slices.Should().ContainSingle().Which.DependsOn.Should().BeNull();
        result.DroppedSliceReasons.Should().ContainSingle().Which.Should().Contain("not an earlier slice");
    }

    [Fact]
    public void Parse_ShouldKeepAValidBackwardDependency()
    {
        var json = """{"intent":"act","slices":[{"capabilityKey":"resolve_location","arguments":{}},{"capabilityKey":"search_knowledge_base","arguments":{},"dependsOn":0}]}""";

        var result = Parser().Parse(json, Available);

        result.Decision.Slices.Should().HaveCount(2);
        result.Decision.Slices[1].DependsOn.Should().Be(0);
        result.DroppedSliceReasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    public void Parse_ShouldDegradeToAnswer_WhenTheResponseIsNotAJsonObject(string content)
    {
        var result = Parser().Parse(content, Available);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeOneOf(TurnDecisionParseFailure.NotJson, TurnDecisionParseFailure.UnrecognisedIntent);
        result.Decision.Should().Be(TurnDecision.AnswerOnly);
    }

    [Theory]
    [InlineData("""{"intent":"do_the_thing","slices":[]}""")]
    [InlineData("""{"slices":[]}""")]
    [InlineData("""{"intent":null,"slices":[]}""")]
    public void Parse_ShouldDegradeToAnswer_OnAnUnrecognisedIntent(string json)
    {
        var result = Parser().Parse(json, Available);

        result.Failure.Should().Be(TurnDecisionParseFailure.UnrecognisedIntent);
        result.Decision.Should().Be(TurnDecision.AnswerOnly);
    }

    [Fact]
    public void Parse_ShouldReportMissingSlices_WhenIntentIsAct()
    {
        // Distinct from an unrecognised intent: the model chose to act and then said nothing
        // about what. Naming the two separately is what lets the retry message be specific.
        var result = Parser().Parse("""{"intent":"act"}""", Available);

        result.Failure.Should().Be(TurnDecisionParseFailure.MissingSlices);
        result.Decision.Should().Be(TurnDecision.AnswerOnly);
    }

    [Fact]
    public void Parse_ShouldDefaultArgumentsToAnEmptyObject_WhenAbsentOrMalformed()
    {
        var json = """{"intent":"act","slices":[{"capabilityKey":"resolve_location"},{"capabilityKey":"search_knowledge_base","arguments":"oops"}]}""";

        var result = Parser().Parse(json, Available);

        result.Decision.Slices.Should().HaveCount(2);
        result.Decision.Slices.Should().OnlyContain(s => s.ArgumentsJson == "{}");
    }

    [Fact]
    public void Parse_ShouldTruncateAnOverlongPendingLabel()
    {
        // The label is UI text shown while work runs; an unbounded one would wreck the layout.
        var longLabel = new string('x', 200);
        var json = $$$"""{"intent":"act","slices":[{"capabilityKey":"resolve_location","arguments":{},"pendingLabel":"{{{longLabel}}}"}]}""";

        var result = Parser().Parse(json, Available);

        result.Decision.Slices[0].PendingLabel!.Length.Should().Be(60);
    }

    [Fact]
    public void Parse_ShouldNeverThrow_ForAnyInput()
    {
        // Constitution §2.VIII at this boundary: a parser that throws takes the user's answer
        // with it, and the whole point of degrading is that they still get one.
        string[] hostile =
        [
            "{", "}", "{\"intent\":", "\0", "{\"intent\":\"act\",\"slices\":{}}",
            "{\"intent\":\"act\",\"slices\":[null]}", "{\"intent\":\"act\",\"slices\":[{}]}",
            "{\"intent\":123,\"slices\":[]}", "{\"intent\":{\"nested\":true},\"slices\":[]}",
            "{\"intent\":\"act\",\"slices\":[{\"capabilityKey\":42}]}",
        ];

        foreach (var content in hostile)
        {
            var act = () => Parser().Parse(content, Available);
            act.Should().NotThrow($"input {content} must degrade rather than throw");
        }
    }
}
