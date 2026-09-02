using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests;

/// <summary>Proves <see cref="AdminDashboardRepository"/>'s aggregates match seeded data (specs/001-admin-dashboard T009).</summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class AdminDashboardRepositoryTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task GetSummaryAsync_ShouldAggregateCountsTrendAndRoles_Correctly()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var superUserRole = new IdentityRole($"Super User-{suffix}") { NormalizedName = $"SUPER USER-{suffix}".ToUpperInvariant() };
            await dbContext.Roles.AddAsync(superUserRole, TestContext.Current.CancellationToken);

            var confirmedActiveUser = new ApplicationUser
            {
                UserName = $"confirmed-{suffix}@example.com",
                Email = $"confirmed-{suffix}@example.com",
                EmailConfirmed = true,
                TwoFactorEnabled = true,
                CreatedAtUtc = now,
            };
            var pendingLockedUser = new ApplicationUser
            {
                UserName = $"pending-{suffix}@example.com",
                Email = $"pending-{suffix}@example.com",
                EmailConfirmed = false,
                LockoutEnabled = true,
                LockoutEnd = DateTimeOffset.MaxValue,
                CreatedAtUtc = now.AddDays(-45), // outside the 30-day trend window
            };
            var superUser = new ApplicationUser
            {
                UserName = $"super-{suffix}@example.com",
                Email = $"super-{suffix}@example.com",
                EmailConfirmed = true,
                CreatedAtUtc = now,
            };

            await dbContext.Users.AddRangeAsync(confirmedActiveUser, pendingLockedUser, superUser);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            await dbContext.UserRoles.AddAsync(new IdentityUserRole<string> { UserId = superUser.Id, RoleId = superUserRole.Id }, TestContext.Current.CancellationToken);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new AdminDashboardRepository(readContext);

        var summary = await repository.GetSummaryAsync(TestContext.Current.CancellationToken);

        var todaysCount = summary.NewUsersLast30Days.Single(d => d.Date == DateOnly.FromDateTime(now));
        todaysCount.NewUsers.Should().BeGreaterThanOrEqualTo(2); // confirmedActiveUser + superUser created "today", possibly alongside other tests' rows

        summary.RoleDistribution.Should().Contain(r => r.RoleName == $"Super User-{suffix}" && r.UserCount == 1);

        var lockedNow = await readContext.Users.CountAsync(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        summary.LockedOutUsers.Should().Be(lockedNow);
    }
}
