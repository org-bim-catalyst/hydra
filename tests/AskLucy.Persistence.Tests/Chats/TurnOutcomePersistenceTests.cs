using AskLucy.Domain.Chats;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// specs/068 T012 — the recorded turn outcome survives a real SQL Server round-trip, and a message
/// written without one still loads (FR-004a, FR-004b).
///
/// <para>
/// The null case matters as much as the populated one: every message written before this column
/// existed has no outcome, and the whole design depends on a null being read as "unknown" rather
/// than as a success.
/// </para>
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class TurnOutcomePersistenceTests(PersistenceTestFixture fixture)
{
    private const string OutcomeJson =
        """{"verdict":"FailedBeforeCompleting","attempts":[{"kind":"resolve_location","key":"resolve_location","targetLabel":"Al Safa Park 2","argumentsJson":"{\"query\":\"Al Safa Park 2\"}","succeeded":false,"failureReason":"Provider credential rejected"}],"failureReason":"Provider credential rejected","recordedAtUtc":"2026-09-24T12:00:00+00:00"}""";

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Message_ShouldRoundTrip_TurnOutcomeJson()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Outcome chat", userId, null, userId);

        var withOutcome = Message.Create(
            chat.Id, MessageRole.Assistant, MessageKind.Text,
            "Something went wrong partway through and I couldn't finish.", null, userId,
            provider: "Anthropic", model: "claude-sonnet-5",
            turnOutcomeJson: OutcomeJson);

        // The legacy shape: an assistant message that predates the column.
        var withoutOutcome = Message.Create(
            chat.Id, MessageRole.Assistant, MessageKind.Text, "Here is the answer", null, userId,
            provider: "Anthropic", model: "claude-sonnet-5");

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            dbContext.Messages.Add(withOutcome);
            dbContext.Messages.Add(withoutOutcome);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var reloaded = await dbContext.Messages
                .SingleAsync(m => m.Id == withOutcome.Id, TestContext.Current.CancellationToken);

            // Byte-identical: the outcome is the authority, so nothing may reshape it in transit.
            reloaded.TurnOutcomeJson.Should().Be(OutcomeJson);

            var legacy = await dbContext.Messages
                .SingleAsync(m => m.Id == withoutOutcome.Id, TestContext.Current.CancellationToken);

            legacy.TurnOutcomeJson.Should().BeNull();
        }
    }
}
