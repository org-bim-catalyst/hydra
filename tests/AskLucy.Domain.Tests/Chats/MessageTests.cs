using System.Linq;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Chats;

public sealed class MessageTests
{
    private static readonly Guid ChatId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldSetMetadataFields()
    {
        var message = Message.Create(
            ChatId, MessageRole.Assistant, MessageKind.Text, "Hello", null, "user-1",
            provider: "OpenAI", model: "gpt-4", generationParametersJson: "{\"temperature\":0.7}",
            inputTokenCount: 12, outputTokenCount: 34);

        message.Provider.Should().Be("OpenAI");
        message.Model.Should().Be("gpt-4");
        message.GenerationParametersJson.Should().Be("{\"temperature\":0.7}");
        message.InputTokenCount.Should().Be(12);
        message.OutputTokenCount.Should().Be(34);
    }

    [Fact]
    public void Create_ShouldSetUsageAndComparisonFields()
    {
        // specs/005-multi-provider-ai-engine FR-020/FR-025.
        var comparisonGroupId = Guid.NewGuid();

        var message = Message.Create(
            ChatId, MessageRole.Assistant, MessageKind.Text, "Hello", null, "user-1",
            provider: "Anthropic", model: "claude-3-5-sonnet-20241022",
            cachedTokenCount: 5, reasoningTokenCount: 10, latencyMs: 842, estimatedCostUsd: 0.0032m,
            comparisonGroupId: comparisonGroupId, isIncludedInContext: false);

        message.CachedTokenCount.Should().Be(5);
        message.ReasoningTokenCount.Should().Be(10);
        message.LatencyMs.Should().Be(842);
        message.EstimatedCostUsd.Should().Be(0.0032m);
        message.ComparisonGroupId.Should().Be(comparisonGroupId);
        message.IsIncludedInContext.Should().BeFalse();
    }

    [Fact]
    public void Create_ShouldDefaultIsIncludedInContextToTrue_ForOrdinaryMessages()
    {
        var message = Message.Create(ChatId, MessageRole.Assistant, MessageKind.Text, "Hello", null, "user-1");

        message.IsIncludedInContext.Should().BeTrue();
        message.ComparisonGroupId.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldLeaveMetadataNull_ForUserMessages()
    {
        var message = Message.Create(ChatId, MessageRole.User, MessageKind.Text, "Hi", null, "user-1");

        message.Provider.Should().BeNull();
        message.Model.Should().BeNull();
        message.GenerationParametersJson.Should().BeNull();
        message.InputTokenCount.Should().BeNull();
        message.OutputTokenCount.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenContentIsBlank(string blankContent)
    {
        var act = () => Message.Create(ChatId, MessageRole.User, MessageKind.Text, blankContent, null, "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void AddAttachment_ShouldAppendToAttachments()
    {
        var message = Message.Create(ChatId, MessageRole.User, MessageKind.Text, "See attached", null, "user-1");

        var attachment = message.AddAttachment("plan.pdf", "application/pdf", "/files/plan.pdf", "user-1");

        message.Attachments.Should().ContainSingle().Which.Should().BeSameAs(attachment);
        attachment.MessageId.Should().Be(message.Id);
    }

    [Fact]
    public void AddCitation_ShouldAppendToCitations()
    {
        var message = Message.Create(ChatId, MessageRole.Assistant, MessageKind.Text, "Per the source...", null, "user-1");

        var citation = message.AddCitation("Company Handbook", "https://example.com/handbook", "user-1");

        message.Citations.Should().ContainSingle().Which.Should().BeSameAs(citation);
        citation.MessageId.Should().Be(message.Id);
    }

    [Fact]
    public void Message_ShouldExposeNoMutationMethodForContent()
    {
        // FR-018: messages are immutable once created — this is a design assertion, not a
        // runtime one: Message intentionally exposes no Edit/Update/SetContent method.
        typeof(Message).GetMethods()
            .Select(m => m.Name)
            .Should().NotContain(name => name.Contains("Edit") || name.Contains("Update") || name == "SetContent");
    }
}
