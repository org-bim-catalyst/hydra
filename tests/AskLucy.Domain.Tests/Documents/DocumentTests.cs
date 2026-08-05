using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Documents;

public sealed class DocumentTests
{
    private static Document CreateDocument() =>
        Document.Create(Guid.CreateVersion7(), "user-1", "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");

    [Fact]
    public void Create_ShouldSetUploadedStatus()
    {
        var document = CreateDocument();

        document.OwnerId.Should().Be("user-1");
        document.FileName.Should().Be("report.pdf");
        document.ProcessingStatus.Should().Be(DocumentProcessingStatus.Uploaded);
        document.ArchivedAtUtc.Should().BeNull();
        document.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenOwnerIdIsBlank(string blankOwnerId)
    {
        var act = () => Document.Create(Guid.CreateVersion7(), blankOwnerId, "report.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Rename_ShouldUpdateFileName()
    {
        var document = CreateDocument();
        document.Rename("renamed.pdf", "user-1");

        document.FileName.Should().Be("renamed.pdf");
    }

    [Fact]
    public void Rename_ShouldThrow_WhenNameIsBlank()
    {
        var document = CreateDocument();
        var act = () => document.Rename("  ", "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Archive_ShouldBeIdempotent()
    {
        var document = CreateDocument();
        document.Archive("user-1");
        var firstArchivedAt = document.ArchivedAtUtc;

        document.Archive("user-1");

        document.ArchivedAtUtc.Should().Be(firstArchivedAt);
    }

    [Fact]
    public void Restore_ShouldUndoArchiveOnly_NotDelete()
    {
        var document = CreateDocument();
        document.Archive("user-1");
        document.SoftDelete("user-1");

        document.Restore("user-1");

        document.ArchivedAtUtc.Should().BeNull("Restore only undoes Archive");
        document.IsDeleted.Should().BeTrue("Restore must not also undo SoftDelete — that's Undelete's job");
    }

    [Fact]
    public void Undelete_ShouldUndoSoftDeleteOnly_NotArchive()
    {
        var document = CreateDocument();
        document.Archive("user-1");
        document.SoftDelete("user-1");

        document.Undelete("user-1");

        document.IsDeleted.Should().BeFalse();
        document.ArchivedAtUtc.Should().NotBeNull("Undelete must not also undo Archive — that's Restore's job");
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAtUtcAndDeletedBy()
    {
        var document = CreateDocument();
        document.SoftDelete("user-1");

        document.IsDeleted.Should().BeTrue();
        document.DeletedBy.Should().Be("user-1");
    }

    [Fact]
    public void SetCurrentVersion_ShouldRepointVersionAndUpdateSize()
    {
        var document = CreateDocument();
        var newVersionId = Guid.CreateVersion7();

        document.SetCurrentVersion(newVersionId, 2048, DocumentFileType.Word, "user-1");

        document.CurrentVersionId.Should().Be(newVersionId);
        document.SizeBytes.Should().Be(2048);
        document.FileType.Should().Be(DocumentFileType.Word);
    }

    [Fact]
    public void IsOwnedBy_ShouldReflectOwnerId()
    {
        var document = CreateDocument();

        document.IsOwnedBy("user-1").Should().BeTrue();
        document.IsOwnedBy("user-2").Should().BeFalse();
    }
}
