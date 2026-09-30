using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>specs/079 - an opened editor becomes a client command; a refusal or a stray result does not.</summary>
public sealed class StructuredPayloadExtractorSiteBoundaryEditTests
{
    private static readonly Guid ChatId = Guid.NewGuid();
    private static readonly Guid Revision = Guid.NewGuid();

    [Fact]
    public void AnOpenedEditor_BecomesASiteBoundaryEditCommandWithTheChatAndRevision()
    {
        var json = $$"""{"siteName":"Muscat Grand Mall","openEditor":true,"chatId":"{{ChatId}}","revision":"{{Revision}}"}""";

        var chunk = StructuredPayloadExtractor.TryExtract(EditSiteBoundaryCapability.CapabilityKey, json);

        chunk.Should().NotBeNull();
        chunk!.SiteBoundaryEdit.Should().NotBeNull();
        chunk.SiteBoundaryEdit!.ChatId.Should().Be(ChatId);
        chunk.SiteBoundaryEdit.Revision.Should().Be(Revision);
    }

    [Fact]
    public void OpenEditorFalse_ProducesNoCommand() =>
        StructuredPayloadExtractor.TryExtract(EditSiteBoundaryCapability.CapabilityKey, """{"openEditor":false}""").Should().BeNull();

    [Fact]
    public void AResultWithoutTheFlag_ProducesNoCommand() =>
        StructuredPayloadExtractor.TryExtract(EditSiteBoundaryCapability.CapabilityKey, """{"siteName":"x"}""").Should().BeNull();

    [Fact]
    public void AMalformedRevision_ProducesNoCommandRatherThanThrowing()
    {
        var json = $$"""{"openEditor":true,"chatId":"{{ChatId}}","revision":"not-a-guid"}""";

        StructuredPayloadExtractor.TryExtract(EditSiteBoundaryCapability.CapabilityKey, json).Should().BeNull();
    }
}
