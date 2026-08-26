using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Chats;

/// <summary>
/// specs/042-site-boundary-resolution T023 — proves <see cref="UserChat.ActiveBoundary"/>
/// (including the polygon ring's JSON value-converter round-trip) survives a real SQL Server
/// save/reload, mirroring <see cref="MessagePersistenceTests"/>'s pattern. FR-009 depends on
/// this: a repeated reference to the same site must be answerable from a reloaded chat without
/// forcing a fresh resolution.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class UserChatActiveBoundaryPersistenceTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task ActiveBoundary_ShouldRoundTrip_IncludingThePolygonRing()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("Boundary chat", userId, null, userId);

        var polygon = new List<GeoPoint>
        {
            new(25.1560, 55.2210), new(25.1560, 55.2220),
            new(25.1550, 55.2220), new(25.1550, 55.2210), new(25.1560, 55.2210),
        };
        chat.SetActiveBoundary(
            "Al Safa Park 2", 25.1560, 55.2218, polygon, areaSquareMeters: 15_000,
            confidence: 0.92, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary,
            "OpenStreetMap (leisure=park)", userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var reloaded = await dbContext.UserChats.SingleAsync(c => c.Id == chat.Id, TestContext.Current.CancellationToken);

            reloaded.ActiveBoundary.Should().NotBeNull();
            reloaded.ActiveBoundary!.SiteName.Should().Be("Al Safa Park 2");
            reloaded.ActiveBoundary.ConfidenceLevel.Should().Be(BoundaryConfidenceLevel.High);
            reloaded.ActiveBoundary.Source.Should().Be(SiteBoundarySource.OsmBoundary);
            reloaded.ActiveBoundary.SourceDetail.Should().Be("OpenStreetMap (leisure=park)");
            reloaded.ActiveBoundary.Polygon.Should().HaveCount(5);
            reloaded.ActiveBoundary.Polygon.Should().BeEquivalentTo(polygon);
        }
    }

    [Fact]
    public async Task ActiveBoundary_ShouldBeNull_WhenNeverSet()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        var chat = UserChat.Create("No boundary yet", userId, null, userId);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
            dbContext.UserChats.Add(chat);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var reloaded = await dbContext.UserChats.SingleAsync(c => c.Id == chat.Id, TestContext.Current.CancellationToken);
            reloaded.ActiveBoundary.Should().BeNull();
        }
    }
}
