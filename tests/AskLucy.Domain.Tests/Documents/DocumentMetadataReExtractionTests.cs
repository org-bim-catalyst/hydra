using AskLucy.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Documents;

/// <summary>US5 — reprocessing a replaced version (<c>MetadataExtractionStageHandler</c>) must never silently clobber a user's manual metadata edit (FR-023, FR-031).</summary>
public sealed class DocumentMetadataReExtractionTests
{
    [Fact]
    public void ApplyReExtraction_ShouldUpdateFields_WhenStillAutoExtracted()
    {
        var metadata = DocumentMetadata.CreateFromExtraction(Guid.CreateVersion7(), "Old Title", "Old Author", null, null, null, null, "system");

        metadata.ApplyReExtraction("New Title", "New Author", null, null, "new keywords", "UTF-8", "system");

        metadata.Title.Should().Be("New Title");
        metadata.Author.Should().Be("New Author");
        metadata.Keywords.Should().Be("new keywords");
        metadata.IsAutoExtracted.Should().BeTrue();
    }

    [Fact]
    public void ApplyReExtraction_ShouldBeANoOp_WhenTheUserAlreadyEditedIt()
    {
        var metadata = DocumentMetadata.CreateFromExtraction(Guid.CreateVersion7(), "Old Title", "Old Author", null, null, null, null, "system");
        metadata.ApplyUserEdit("User's Title", "User's Author", null, null, "user keywords", "user-1");

        metadata.ApplyReExtraction("Freshly Extracted Title", "Someone Else", null, null, "extracted keywords", "UTF-8", "system");

        metadata.Title.Should().Be("User's Title");
        metadata.Author.Should().Be("User's Author");
        metadata.Keywords.Should().Be("user keywords");
        metadata.IsAutoExtracted.Should().BeFalse();
    }
}
