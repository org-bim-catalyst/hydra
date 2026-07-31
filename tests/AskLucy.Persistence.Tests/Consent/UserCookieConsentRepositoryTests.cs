using AskLucy.Domain.Consent;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AskLucy.Persistence.Tests.Consent;

/// <summary>
/// Proves the append-only repository's "latest row" / "full history" queries and the
/// cascade-delete FK added in <see cref="Configurations.CookieConsentRecordConfiguration"/>
/// (specs/004-cookie-consent-privacy) against a real SQL Server instance.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class UserCookieConsentRepositoryTests(PersistenceTestFixture fixture)
{
    private static async Task<string> SeedUserAsync(AskLucyDbContext dbContext)
    {
        var email = $"consent-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, CreatedAtUtc = DateTime.UtcNow };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task GetLatestAsync_ShouldReturnTheMostRecentlyCreatedRecord_AcrossMultipleInserts()
    {
        string userId;
        await using (var dbContext = fixture.CreateDbContext())
        {
            userId = await SeedUserAsync(dbContext);
        }

        var repository = new UserCookieConsentRepository(fixture.CreateDbContext());

        await repository.AddAsync(CookieConsentRecord.Create(userId, "2026-01-01.1", false, false, false));
        await Task.Delay(10); // guarantee a distinct CreatedAtUtc ordering, since both rows are otherwise inserted in the same test
        await repository.AddAsync(CookieConsentRecord.Create(userId, "2026-07-30.1", true, true, false));

        var latest = await repository.GetLatestAsync(userId);

        latest.Should().NotBeNull();
        latest!.PolicyVersion.Should().Be("2026-07-30.1");
        latest.FunctionalAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task GetHistoryAsync_ShouldReturnEveryRecord_OrderedByCreatedAtUtcDescending()
    {
        string userId;
        await using (var dbContext = fixture.CreateDbContext())
        {
            userId = await SeedUserAsync(dbContext);
        }

        var repository = new UserCookieConsentRepository(fixture.CreateDbContext());
        await repository.AddAsync(CookieConsentRecord.Create(userId, "2026-01-01.1", false, false, false));
        await Task.Delay(10);
        await repository.AddAsync(CookieConsentRecord.Create(userId, "2026-07-30.1", true, true, true));

        var history = await repository.GetHistoryAsync(userId);

        history.Should().HaveCount(2);
        history[0].PolicyVersion.Should().Be("2026-07-30.1");
        history[1].PolicyVersion.Should().Be("2026-01-01.1");
    }

    [Fact]
    public async Task DeletingTheOwningUser_ShouldCascadeAndRemoveAllOfThatUsersConsentRecords()
    {
        string userId;
        await using (var dbContext = fixture.CreateDbContext())
        {
            userId = await SeedUserAsync(dbContext);
        }

        var repository = new UserCookieConsentRepository(fixture.CreateDbContext());
        await repository.AddAsync(CookieConsentRecord.Create(userId, "2026-07-30.1", true, false, false));

        await using (var dbContext = fixture.CreateDbContext())
        {
            var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var remaining = await dbContext.CookieConsentRecords.Where(c => c.UserId == userId).ToListAsync();
            remaining.Should().BeEmpty();
        }
    }
}
