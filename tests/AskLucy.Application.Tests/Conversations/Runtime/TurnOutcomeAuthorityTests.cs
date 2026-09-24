using System.Reflection;
using System.Text.RegularExpressions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;
using MessageEntity = AskLucy.Domain.Chats.Message;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 T020, FR-004b/FR-004f — the recorded turn outcome hangs off the <b>message</b>, and
/// the <c>AgentExecution</c> audit trail is never read back as its source.
///
/// <para>The distinction is not stylistic. An <see cref="Domain.Agents.AgentExecution"/> carries a
/// chat id but no message id, so it cannot say which reply a user is looking at — reading a turn's
/// outcome from it would mean guessing by recency, which is exactly the class of guess that
/// produced the false "I've shown you Al Safa Park 2." Reply composition, the claim gate and retry
/// resolution therefore read <c>Message.TurnOutcomeJson</c>, and nothing else.</para>
///
/// <para>Structural, because it is a rule about where future code may look, not about behaviour
/// any one method exhibits today: a unit test can only prove the current readers are honest,
/// whereas this fails the moment a new one reaches for the trail.</para>
/// </summary>
public sealed class TurnOutcomeAuthorityTests
{
    /// <summary>
    /// The execution-record model, minus <c>AgentExecutionPolicy</c> — a policy is configuration
    /// consulted before a run, not a record of one, and the conversational runtime legitimately
    /// evaluates it.
    /// </summary>
    private static readonly Regex AuditTrailTypeReference = new(
        @"\bI?AgentExecution(?!Policy)\w*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The sole writer. It has no read API — proved separately below.</summary>
    private const string TheOnlyFileAllowedToTouchTheTrail = "TurnRecorder.cs";

    [Fact]
    public void TheTurnPath_ShouldReferenceTheAuditTrail_FromTheRecorderAlone()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in TurnPathSourceFiles(repoRoot))
        {
            if (Path.GetFileName(file) == TheOnlyFileAllowedToTouchTheTrail)
            {
                continue;
            }

            var code = StripComments(File.ReadAllText(file));
            violations.AddRange(
                AuditTrailTypeReference.Matches(code)
                    .Select(match => $"{Path.GetFileName(file)}: references '{match.Value}' — read Message.TurnOutcomeJson instead (FR-004f)"));
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void EveryOutcomeReader_ShouldReadItFromTheMessage()
    {
        var repoRoot = FindRepoRoot();

        // Matched by filename so the rule binds the claim gate, the routing summary and the retry
        // resolver the moment each lands, rather than needing this test amended alongside them.
        var readers = TurnPathSourceFiles(repoRoot)
            .Where(file => Path.GetFileName(file) is var name
                && (name.Contains("ClaimGate", StringComparison.Ordinal)
                    || name.Contains("RetryTarget", StringComparison.Ordinal)
                    || name.Contains("RecentTurnOutcome", StringComparison.Ordinal)))
            .ToList();

        var violations = readers
            .Where(file =>
            {
                var code = StripComments(File.ReadAllText(file));
                return !code.Contains("TurnOutcomeJson", StringComparison.Ordinal)
                    && !code.Contains(nameof(RecordedTurnOutcome), StringComparison.Ordinal);
            })
            .Select(file => $"{Path.GetFileName(file)}: reads neither Message.TurnOutcomeJson nor a {nameof(RecordedTurnOutcome)} (FR-004b)")
            .ToList();

        violations.Should().BeEmpty();
    }

    [Fact]
    public void TheMessageAggregate_ShouldCarryTheOutcome_AsAppendOnlyState()
    {
        var property = typeof(MessageEntity).GetProperty(nameof(MessageEntity.TurnOutcomeJson));

        property.Should().NotBeNull("the message is the authority FR-004b names");
        property!.PropertyType.Should().Be<string>();
        property.SetMethod.Should().NotBeNull();
        property.SetMethod!.IsPublic.Should().BeFalse("the outcome is set at creation — the aggregate is append-only");
    }

    [Fact]
    public void TheStreamChunk_ShouldCarryTheRecordedOutcome()
    {
        var property = typeof(ChatStreamChunk).GetProperty(nameof(ChatStreamChunk.TurnOutcome));

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be<RecordedTurnOutcome?>();
    }

    [Fact]
    public void TheRecorder_ShouldExposeNoWayToReadTheTrailBack()
    {
        var publicMethods = typeof(TurnRecorder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToList();

        // Write-only by construction: no query method exists to be tempted by, so FR-004f cannot be
        // violated through this type even by a caller that wanted to.
        publicMethods.Should().Equal(nameof(TurnRecorder.RecordAsync));
    }

    private static IEnumerable<string> TurnPathSourceFiles(string repoRoot)
    {
        string[] directories =
        [
            Path.Combine(repoRoot, "src", "AskLucy.Application", "Conversations", "Runtime"),
            Path.Combine(repoRoot, "src", "AskLucy.Application", "Ai"),
        ];

        foreach (var file in directories.Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)))
        {
            yield return file;
        }

        var chatController = Path.Combine(repoRoot, "src", "AskLucy.Web", "Controllers", "v1", "AiController.cs");
        if (File.Exists(chatController))
        {
            yield return chatController;
        }
    }

    /// <summary>
    /// Line-oriented, matching this codebase's convention of <c>///</c> and <c>//</c> comments only.
    /// The trail is discussed at length in prose here — <c>&lt;see cref="AgentExecution"/&gt;</c> in
    /// a doc comment is documentation, not a dependency.
    /// </summary>
    private static string StripComments(string source) =>
        string.Join('\n', source
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ask Lucy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (Ask Lucy.sln not found above the test output directory).");
    }
}
