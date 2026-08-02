using AskLucy.Domain.Chats;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// Proves a message's full metadata (provider/model/tokens/generation params) plus its
/// attachments/citations round-trip through a real SQL Server instance unchanged
/// (specs/002-chat-history-management User Story 1, FR-016/FR-017).
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class MessagePersistenceTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task Message_ShouldRoundTrip_MetadataAttachmentsAndCitations()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Persisted chat", userId, null, userId);

        var message = Message.Create(
            chat.Id, MessageRole.Assistant, MessageKind.Text, "Here is the answer", null, userId,
            provider: "OpenAI", model: "gpt-4", generationParametersJson: "{\"temperature\":0.2}",
            inputTokenCount: 42, outputTokenCount: 128);
        message.AddAttachment("report.pdf", "application/pdf", "/files/report.pdf", userId);
        message.AddCitation("Company Handbook", "https://example.com/handbook", userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            dbContext.Messages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var reloaded = await dbContext.Messages
                .Include(m => m.Attachments)
                .Include(m => m.Citations)
                .SingleAsync(m => m.Id == message.Id);

            reloaded.Provider.Should().Be("OpenAI");
            reloaded.Model.Should().Be("gpt-4");
            reloaded.GenerationParametersJson.Should().Be("{\"temperature\":0.2}");
            reloaded.InputTokenCount.Should().Be(42);
            reloaded.OutputTokenCount.Should().Be(128);
            reloaded.Attachments.Should().ContainSingle(a => a.FileName == "report.pdf" && a.AccessLocation == "/files/report.pdf");
            reloaded.Citations.Should().ContainSingle(c => c.SourceLabel == "Company Handbook");
        }
    }
}
