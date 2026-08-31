using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests;

/// <summary>
/// Proves the global query filter added by <see cref="Configurations.ApplicationUserConfiguration"/>
/// (specs/001-admin-dashboard T003) transparently excludes soft-deleted users from every read
/// through <see cref="AskLucyDbContext.Users"/> — which is what <c>UserAdminRepository</c> and
/// ASP.NET Identity's <c>UserManager</c> both read through (research.md Topic 1).
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class ApplicationUserQueryFilterTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task Users_query_excludes_soft_deleted_user_by_default()
    {
        var activeEmail = $"active-{Guid.NewGuid():N}@example.com";
        var deletedEmail = $"deleted-{Guid.NewGuid():N}@example.com";

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.AddRange(
                new ApplicationUser { UserName = activeEmail, Email = activeEmail, CreatedAtUtc = DateTime.UtcNow, IsDeleted = false },
                new ApplicationUser { UserName = deletedEmail, Email = deletedEmail, CreatedAtUtc = DateTime.UtcNow, IsDeleted = true, DeletedAtUtc = DateTime.UtcNow, DeletedBy = "test-admin" });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var emails = await dbContext.Users.Select(u => u.Email).ToListAsync(TestContext.Current.CancellationToken);

            emails.Should().Contain(activeEmail);
            emails.Should().NotContain(deletedEmail);
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_still_surfaces_the_soft_deleted_user()
    {
        var deletedEmail = $"deleted-{Guid.NewGuid():N}@example.com";

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(new ApplicationUser
            {
                UserName = deletedEmail,
                Email = deletedEmail,
                CreatedAtUtc = DateTime.UtcNow,
                IsDeleted = true,
                DeletedAtUtc = DateTime.UtcNow,
                DeletedBy = "test-admin",
            });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var found = await dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == deletedEmail, TestContext.Current.CancellationToken);

            found.Should().NotBeNull();
            found!.IsDeleted.Should().BeTrue();
        }
    }
}
