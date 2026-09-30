using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.SiteBoundaries;

/// <summary>
/// specs/079 T014 — the correction's JSON columns round-trip through a real SQL Server, the
/// soft-delete filter hides deleted rows, every read is scoped to the owning user (FR-026), and the
/// new <c>UserChats</c> columns survive a save. Needs the test2 database migrated by hand.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class SiteBoundaryCorrectionRepositoryTests(PersistenceTestFixture fixture)
{
    private static readonly IReadOnlyList<GeoPoint> MainRing =
    [
        new(25.156012345678, 55.221012345678), new(25.156012345678, 55.222012345678),
        new(25.155012345678, 55.222012345678), new(25.155012345678, 55.221012345678),
    ];

    private static readonly IReadOnlyList<GeoPoint> SecondRing =
        [new(25.157, 55.223), new(25.157, 55.2235), new(25.1565, 55.2235)];

    private static SiteBoundaryCorrection NewCorrection(string userId, string siteName = "Muscat Grand Mall")
    {
        var member = new SiteBoundaryMember(
            "osm_way_1", "Phase 2", SiteBoundaryMemberKind.Building, SiteBoundaryMemberRelation.Nearby, 12.5, SecondRing, true);
        var snapshot = new FoundSiteBoundarySnapshot(
            MainRing, [SecondRing], MainRing, 15_000, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", [member]);

        return SiteBoundaryCorrection.Create(
            userId, siteName, 25.1555, 55.2215, snapshot, [MainRing, SecondRing], 14_321.5, [member], userId);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Correction_ShouldRoundTrip_RingsSnapshotAndMembers()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var correction = NewCorrection(userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            new SiteBoundaryCorrectionRepository(dbContext).Add(correction);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var reloaded = await new SiteBoundaryCorrectionRepository(dbContext)
                .GetByIdAsync(correction.Id, userId, TestContext.Current.CancellationToken);

            reloaded.Should().NotBeNull();
            reloaded!.NormalizedSiteName.Should().Be("muscat grand mall");
            reloaded.Revision.Should().Be(correction.Revision);
            reloaded.AreaSquareMeters.Should().Be(14_321.5);
            reloaded.EditedRings.Should().HaveCount(2);
            reloaded.EditedRings[0].Should().BeEquivalentTo(MainRing, o => o.Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-9)).WhenTypeIs<double>());
            reloaded.FoundSnapshot.Source.Should().Be(SiteBoundarySource.OsmBoundary);
            reloaded.FoundSnapshot.CorePolygon.Should().NotBeNull();
            reloaded.Members.Should().ContainSingle().Which.Kind.Should().Be(SiteBoundaryMemberKind.Building);
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Reads_ShouldBeScopedToTheOwningUser()
    {
        var owner = $"owner-{Guid.NewGuid():N}";
        var other = $"other-{Guid.NewGuid():N}";
        var correction = NewCorrection(owner);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(owner));
            new SiteBoundaryCorrectionRepository(dbContext).Add(correction);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new SiteBoundaryCorrectionRepository(dbContext);

            (await repository.GetByIdAsync(correction.Id, other, TestContext.Current.CancellationToken)).Should().BeNull();
            (await repository.FindCandidatesAsync(other, "muscat grand mall", TestContext.Current.CancellationToken)).Should().BeEmpty();
            (await repository.FindCandidatesAsync(owner, "muscat grand mall", TestContext.Current.CancellationToken)).Should().ContainSingle();
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task DeletedCorrection_ShouldBeInvisible()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var correction = NewCorrection(userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            new SiteBoundaryCorrectionRepository(dbContext).Add(correction);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var tracked = await new SiteBoundaryCorrectionRepository(dbContext)
                .GetByIdAsync(correction.Id, userId, TestContext.Current.CancellationToken);
            tracked!.Delete(userId);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new SiteBoundaryCorrectionRepository(dbContext);

            (await repository.GetByIdAsync(correction.Id, userId, TestContext.Current.CancellationToken)).Should().BeNull();
            (await repository.FindCandidatesAsync(userId, "muscat grand mall", TestContext.Current.CancellationToken)).Should().BeEmpty();
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task UserChat_ShouldRoundTrip_RevisionAndCorrectionLink()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Boundary chat", userId, null, userId);
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.1555, 55.2215, MainRing, 15_000, 0.7,
            BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", userId);
        var correctionId = Guid.NewGuid();
        chat.LinkSiteBoundaryCorrection(correctionId, userId);
        var revision = chat.ActiveBoundary!.Revision;

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var reloaded = await dbContext.UserChats.SingleAsync(c => c.Id == chat.Id, TestContext.Current.CancellationToken);

            reloaded.ActiveBoundary!.Revision.Should().Be(revision);
            reloaded.ActiveBoundary.CorrectionId.Should().Be(correctionId);
        }
    }
}
