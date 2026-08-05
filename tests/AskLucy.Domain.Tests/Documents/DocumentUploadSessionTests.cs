using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Documents;

public sealed class DocumentUploadSessionTests
{
    private static DocumentUploadSession CreateSession() =>
        DocumentUploadSession.Create("user-1", "report.pdf", 1024, 256, DateTime.UtcNow.AddHours(24), "user-1");

    [Fact]
    public void Create_ShouldStartInProgress()
    {
        var session = CreateSession();

        session.Status.Should().Be(DocumentUploadSessionStatus.InProgress);
        session.OwnerId.Should().Be("user-1");
    }

    [Fact]
    public void EnsureInProgress_ShouldThrow_WhenNotInProgress()
    {
        var session = CreateSession();
        session.Cancel("user-1");

        var act = session.EnsureInProgress;
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void MarkPendingDuplicateResolution_ShouldStoreFileNameAndHash()
    {
        var session = CreateSession();
        session.MarkPendingDuplicateResolution("stored-file.bin", "abc123", "user-1");

        session.Status.Should().Be(DocumentUploadSessionStatus.PendingDuplicateResolution);
        session.PendingStoredFileName.Should().Be("stored-file.bin");
        session.PendingChecksumHash.Should().Be("abc123");
    }

    [Fact]
    public void MarkPendingDuplicateResolution_ShouldThrow_WhenNotInProgress()
    {
        var session = CreateSession();
        session.Complete("user-1");

        var act = () => session.MarkPendingDuplicateResolution("stored-file.bin", "abc123", "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }
}
