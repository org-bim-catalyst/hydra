using AskLucy.Domain.Authentication;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Authentication;

/// <summary>
/// Exercises <see cref="PasswordResetTokenRepository"/> against real SQL Server
/// (specs/058-password-recovery T012, constitution &#167;10): the unique hash index, the
/// supersede-pending sweep and the throttle window are all schema behaviour an in-memory provider
/// would not reproduce faithfully.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class PasswordResetTokenRepositoryTests(PersistenceTestFixture fixture)
{
    private static string NewHash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private async Task<string> CreateUserAsync()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";

        await using var dbContext = fixture.CreateDbContext();
        var user = new ApplicationUser { UserName = email, Email = email, CreatedAtUtc = DateTime.UtcNow };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return user.Id;
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task FindByHashAsync_returns_the_issued_token()
    {
        var userId = await CreateUserAsync();
        var hash = NewHash();

        await using (var dbContext = fixture.CreateDbContext())
        {
            new PasswordResetTokenRepository(dbContext)
                .Add(PasswordResetToken.IssueNew(userId, hash, "reset@example.com", TimeSpan.FromHours(1), "203.0.113.5"));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var found = await new PasswordResetTokenRepository(dbContext)
                .FindByHashAsync(hash, TestContext.Current.CancellationToken);

            found.Should().NotBeNull();
            found!.UserId.Should().Be(userId);
            found.IsRedeemable.Should().BeTrue();
            found.RequestedFromIp.Should().Be("203.0.113.5");
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task FindByHashAsync_still_returns_a_consumed_token_so_the_caller_can_reject_it()
    {
        // The repository deliberately does not filter by state: the handler must be the one that
        // decides, so "no such token" and "spent token" stay indistinguishable to the user.
        var userId = await CreateUserAsync();
        var hash = NewHash();

        await using (var dbContext = fixture.CreateDbContext())
        {
            var token = PasswordResetToken.IssueNew(userId, hash, "reset@example.com", TimeSpan.FromHours(1));
            token.Consume();
            new PasswordResetTokenRepository(dbContext).Add(token);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var found = await new PasswordResetTokenRepository(dbContext)
                .FindByHashAsync(hash, TestContext.Current.CancellationToken);

            found.Should().NotBeNull();
            found!.IsRedeemable.Should().BeFalse();
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task SupersedePendingForUserAsync_invalidates_pending_tokens_but_leaves_consumed_ones_untouched()
    {
        var userId = await CreateUserAsync();
        var otherUserId = await CreateUserAsync();
        var pendingHash = NewHash();
        var consumedHash = NewHash();
        var otherUserHash = NewHash();
        DateTime consumedAt;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var consumed = PasswordResetToken.IssueNew(userId, consumedHash, "reset@example.com", TimeSpan.FromHours(1));
            consumed.Consume();
            consumedAt = consumed.ConsumedAtUtc!.Value;

            var repository = new PasswordResetTokenRepository(dbContext);
            repository.Add(PasswordResetToken.IssueNew(userId, pendingHash, "reset@example.com", TimeSpan.FromHours(1)));
            repository.Add(consumed);
            repository.Add(PasswordResetToken.IssueNew(otherUserId, otherUserHash, "other@example.com", TimeSpan.FromHours(1)));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new PasswordResetTokenRepository(dbContext);
            await repository.SupersedePendingForUserAsync(userId, cancellationToken: TestContext.Current.CancellationToken);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new PasswordResetTokenRepository(dbContext);

            (await repository.FindByHashAsync(pendingHash, TestContext.Current.CancellationToken))!
                .SupersededAtUtc.Should().NotBeNull();

            var consumed = (await repository.FindByHashAsync(consumedHash, TestContext.Current.CancellationToken))!;
            consumed.SupersededAtUtc.Should().BeNull();
            consumed.ConsumedAtUtc.Should().BeCloseTo(consumedAt, TimeSpan.FromSeconds(1));

            // Supersession must never spill across accounts.
            (await repository.FindByHashAsync(otherUserHash, TestContext.Current.CancellationToken))!
                .SupersededAtUtc.Should().BeNull();
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task CountIssuedSinceAsync_counts_only_this_user_inside_the_window()
    {
        var userId = await CreateUserAsync();
        var otherUserId = await CreateUserAsync();
        var windowStart = DateTime.UtcNow;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new PasswordResetTokenRepository(dbContext);
            repository.Add(PasswordResetToken.IssueNew(userId, NewHash(), "reset@example.com", TimeSpan.FromHours(1)));
            repository.Add(PasswordResetToken.IssueNew(userId, NewHash(), "reset@example.com", TimeSpan.FromHours(1)));
            repository.Add(PasswordResetToken.IssueNew(otherUserId, NewHash(), "other@example.com", TimeSpan.FromHours(1)));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new PasswordResetTokenRepository(dbContext);

            (await repository.CountIssuedSinceAsync(userId, windowStart, TestContext.Current.CancellationToken))
                .Should().Be(2);

            // A window opening after the tokens were issued sees none of them, which is what lets
            // the throttle expire.
            (await repository.CountIssuedSinceAsync(userId, DateTime.UtcNow.AddMinutes(5), TestContext.Current.CancellationToken))
                .Should().Be(0);
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Deleting_the_account_cascades_the_tokens_away()
    {
        var userId = await CreateUserAsync();
        var hash = NewHash();

        await using (var dbContext = fixture.CreateDbContext())
        {
            new PasswordResetTokenRepository(dbContext)
                .Add(PasswordResetToken.IssueNew(userId, hash, "reset@example.com", TimeSpan.FromHours(1)));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var user = await dbContext.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            (await new PasswordResetTokenRepository(dbContext).FindByHashAsync(hash, TestContext.Current.CancellationToken))
                .Should().BeNull();
        }
    }
}
