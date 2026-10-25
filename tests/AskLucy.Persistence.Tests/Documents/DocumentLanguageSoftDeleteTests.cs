using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AskLucy.Persistence.Tests.Documents;

/// <summary>
/// The soft-delete contract for <see cref="DocumentLanguage"/> against a real SQL Server instance.
/// <para>
/// Reprocessing a replaced version (specs/015 US5) clears the previous language set through
/// <c>DocumentRepository.RemoveLanguages</c>, which is a <c>RemoveRange</c> — and
/// <c>AuditSaveChangesInterceptor</c> turns every <c>BaseEntity</c> delete into a soft delete. When
/// the entity declares no global query filter those "removed" rows stay readable, so
/// <c>GetLanguagesByDocumentIdAsync</c> returns them alongside the fresh set and the document
/// accumulates languages on every reprocess. A substituted repository cannot show this: the bug is
/// entirely in what EF Core generates for the index/filter, which is why this runs here.
/// </para>
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class DocumentLanguageSoftDeleteTests(PersistenceTestFixture fixture)
{
    /// <summary>
    /// A fresh owner per test: the fixture empties the database once per run, not between tests, so a
    /// shared constant primary key would collide on the second insert into AspNetUsers.
    /// </summary>
    private static string NewOwnerId() => $"owner-languages-{Guid.NewGuid():N}";

    private async Task<Guid> SeedDocumentWithLanguagesAsync(
        string ownerId, CancellationToken cancellationToken, params string[] languageCodes)
    {
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(ownerId));

        var versionId = Guid.CreateVersion7();
        var checksum = DocumentChecksum.Create($"{Guid.NewGuid():N}{Guid.NewGuid():N}"[..64], ownerId);
        var document = Document.Create(Guid.CreateVersion7(), ownerId, "site.pdf", DocumentFileType.Pdf, 1024, versionId, ownerId);
        var version = DocumentVersion.Create(document.Id, 1, 0, "stored.bin", "site.pdf", 1024, checksum.Id, ownerId);

        dbContext.Documents.Add(document);
        dbContext.DocumentChecksums.Add(checksum);
        dbContext.DocumentVersions.Add(version);

        foreach (var code in languageCodes)
        {
            dbContext.DocumentLanguages.Add(
                DocumentLanguage.Create(document.Id, code, DocumentLanguageRole.Primary, 0.9m, ownerId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task RemoveLanguages_ShouldHideTheSoftDeletedRows_FromSubsequentReads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var documentId = await SeedDocumentWithLanguagesAsync(NewOwnerId(), cancellationToken, "en", "ar");

        // Exactly what ClassificationStageHandler does on reprocessing: read the current set, remove
        // it, then add the fresh one — through the audit interceptor, so the removal is a soft delete.
        await using (var audited = fixture.CreateAuditedDbContext(
            Substitute.For<ICurrentUserAccessor>(), Substitute.For<ICorrelationIdAccessor>()))
        {
            var repository = new DocumentRepository(audited);
            var existing = await repository.GetLanguagesByDocumentIdAsync(documentId, cancellationToken);
            existing.Should().HaveCount(2);

            repository.RemoveLanguages(existing);
            await audited.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var afterRemoval = await new DocumentRepository(readContext).GetLanguagesByDocumentIdAsync(documentId, cancellationToken);

        afterRemoval.Should().BeEmpty(
            "a removed language is soft-deleted, and the query filter must keep it out of every read — " +
            "otherwise each reprocess appends another set to the same document");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task RemoveLanguages_ShouldLeaveLiveLanguagesAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = NewOwnerId();
        var documentId = await SeedDocumentWithLanguagesAsync(ownerId, cancellationToken, "en", "ar");

        await using (var audited = fixture.CreateAuditedDbContext(
            Substitute.For<ICurrentUserAccessor>(), Substitute.For<ICorrelationIdAccessor>()))
        {
            var repository = new DocumentRepository(audited);
            var existing = await repository.GetLanguagesByDocumentIdAsync(documentId, cancellationToken);
            repository.RemoveLanguages(existing.Where(l => l.LanguageCode == "ar"));
            await audited.SaveChangesAsync(cancellationToken);

            repository.AddLanguage(DocumentLanguage.Create(documentId, "fr", DocumentLanguageRole.Primary, 1m, ownerId));
            await audited.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var remaining = await new DocumentRepository(readContext).GetLanguagesByDocumentIdAsync(documentId, cancellationToken);

        remaining.Select(l => l.LanguageCode).Should().BeEquivalentTo(["en", "fr"]);
    }
}
