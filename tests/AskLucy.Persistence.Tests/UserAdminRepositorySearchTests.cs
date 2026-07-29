using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests;

/// <summary>FR-009/010/011 (specs/001-admin-dashboard) against a real SQL Server instance.</summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class UserAdminRepositorySearchTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task SearchAsync_ShouldFilterByPartialEmailOrName_SortAndPaginate_Correctly()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.AddRange(
                new ApplicationUser { UserName = $"jane.doe-{suffix}@example.com", Email = $"jane.doe-{suffix}@example.com", FirstName = "Jane", CreatedAtUtc = now.AddDays(-2) },
                new ApplicationUser { UserName = $"john.smith-{suffix}@example.com", Email = $"john.smith-{suffix}@example.com", FirstName = "John", CreatedAtUtc = now.AddDays(-1) },
                new ApplicationUser { UserName = $"unrelated-{suffix}@example.com", Email = $"unrelated-{suffix}@example.com", FirstName = "Alex", CreatedAtUtc = now });

            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserAdminRepository(readContext);

        var searchResult = await repository.SearchAsync(search: $"jane.doe-{suffix}", sortBy: "email", sortDescending: false, page: 1, pageSize: 20);
        searchResult.TotalCount.Should().Be(1);
        searchResult.Items.Single().Email.Should().Be($"jane.doe-{suffix}@example.com");

        var sortedResult = await repository.SearchAsync(
            search: suffix, sortBy: "createdAtUtc", sortDescending: true, page: 1, pageSize: 20);
        sortedResult.TotalCount.Should().Be(3);
        sortedResult.Items.Select(u => u.FirstName).Should().ContainInOrder("Alex", "John", "Jane");

        var page2 = await repository.SearchAsync(search: suffix, sortBy: "createdAtUtc", sortDescending: true, page: 2, pageSize: 2);
        page2.TotalCount.Should().Be(3);
        page2.Items.Should().HaveCount(1);
        page2.Items.Single().FirstName.Should().Be("Jane");
    }

    [Fact]
    public async Task SearchAsync_ShouldExcludeSoftDeletedUsers()
    {
        var suffix = Guid.NewGuid().ToString("N");

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(new ApplicationUser
            {
                UserName = $"deleted-{suffix}@example.com",
                Email = $"deleted-{suffix}@example.com",
                CreatedAtUtc = DateTime.UtcNow,
                IsDeleted = true,
                DeletedAtUtc = DateTime.UtcNow,
                DeletedBy = "test-admin",
            });
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new UserAdminRepository(readContext);

        var result = await repository.SearchAsync(search: suffix, sortBy: "email", sortDescending: false, page: 1, pageSize: 20);

        result.TotalCount.Should().Be(0);
    }
}
