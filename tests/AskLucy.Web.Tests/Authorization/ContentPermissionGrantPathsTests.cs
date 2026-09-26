using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Authorization;

/// <summary>
/// specs/074 T080 (FR-016f, FR-016g, FR-016k) — every path that can grant or remove View user
/// content, end to end: an Administrator gets a 403 whose detail names the reason and nothing is
/// written; a Super User succeeds and the change lands in the role audit trail. The actors are
/// synthetic token subjects (their role claim is what the handlers read); the roles and the users
/// they're assigned to are real rows, created through the API as a Super User and removed after.
/// </summary>
public sealed class ContentPermissionGrantPathsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private const string RefusalDetail = "Only a Super User can grant or remove View user content.";
    private const string ContentView = AdminPermissionCatalog.OperationalFailuresContentView;
    private const string PlainKey = "admin.dashboard.view";

    private static readonly RoleAuditAction[] WriteActions =
    [
        RoleAuditAction.RoleCreated, RoleAuditAction.RoleUpdated, RoleAuditAction.RoleDeleted,
        RoleAuditAction.RoleAssigned, RoleAuditAction.RoleChanged, RoleAuditAction.RoleRemoved,
    ];

    private readonly HttpClient _client = factory.CreateClient();
    private readonly string _administratorId = $"cpg-admin-{Guid.NewGuid():N}";
    private readonly string _superUserId = $"cpg-super-{Guid.NewGuid():N}";

    public static TheoryData<string> Paths =>
    [
        "create", "update-add", "update-omit", "delete", "bulk-delete",
        "assign", "replace", "bulk-assign", "bulk-assign-over-holder", "legacy-change-role", "administrator-switch",
    ];

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Administrator_IsRefusedWithTheReason_AndNothingIsWritten(string path)
    {
        var seed = await SeedAsync();
        try
        {
            AuthenticateAs(TestJwtFactory.Create(_administratorId, PrivilegedRoleNames.Administrator));

            using var response = await SendAsync(path, seed);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            using var problem = await ReadJsonAsync(response);
            problem.RootElement.GetProperty("detail").GetString().Should().Be(RefusalDetail);
            (await CountWritesByAsync(_administratorId)).Should().Be(0);
            (await HoldsContentRoleAsync(seed)).Should().BeTrue("the refused change never reached the store");
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task SuperUser_Succeeds_AndTheChangeIsAudited(string path)
    {
        var seed = await SeedAsync();
        try
        {
            var writesBefore = await CountWritesByAsync(_superUserId);

            using var response = await SendAsync(path, seed);

            response.IsSuccessStatusCode.Should().BeTrue($"a Super User may use the {path} path (got {(int)response.StatusCode})");
            (await CountWritesByAsync(_superUserId)).Should().BeGreaterThan(writesBefore);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private sealed record Seed(
        RoleSnapshot ContentRole, RoleSnapshot PlainRole, string TargetUserId, string HolderUserId, bool AdministratorContentAccess);

    private sealed record RoleSnapshot(string Id, string ConcurrencyStamp);

    private void AuthenticateAs(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private void AuthenticateAsSuperUser() => AuthenticateAs(TestJwtFactory.Create(_superUserId, PrivilegedRoleNames.SuperUser));

    private Task<HttpResponseMessage> SendAsync(string path, Seed seed)
    {
        var ct = TestContext.Current.CancellationToken;
        return path switch
        {
            "create" => _client.PostAsJsonAsync(
                "/api/v1/admin/roles", new { name = UniqueRoleName("created"), description = (string?)null, permissionKeys = new[] { ContentView } }, ct),
            "update-add" => _client.PutAsJsonAsync(
                $"/api/v1/admin/roles/{seed.PlainRole.Id}",
                new { name = UniqueRoleName("plain"), description = (string?)null, permissionKeys = new[] { PlainKey, ContentView }, concurrencyStamp = seed.PlainRole.ConcurrencyStamp }, ct),
            "update-omit" => _client.PutAsJsonAsync(
                $"/api/v1/admin/roles/{seed.ContentRole.Id}",
                new { name = UniqueRoleName("content"), description = (string?)null, permissionKeys = new[] { PlainKey }, concurrencyStamp = seed.ContentRole.ConcurrencyStamp }, ct),
            "delete" => _client.DeleteAsync($"/api/v1/admin/roles/{seed.ContentRole.Id}?concurrencyStamp={Uri.EscapeDataString(seed.ContentRole.ConcurrencyStamp)}", ct),
            "bulk-delete" => _client.PostAsJsonAsync(
                "/api/v1/admin/roles/actions/bulk-delete", new { ids = new[] { seed.ContentRole.Id }, allMatching = false }, ct),
            "assign" => _client.PutAsJsonAsync(
                $"/api/v1/admin/role-assignments/{seed.TargetUserId}", new { roleId = seed.ContentRole.Id, expectedCurrentRoleId = (string?)null }, ct),
            "replace" => _client.PutAsJsonAsync(
                $"/api/v1/admin/role-assignments/{seed.HolderUserId}", new { roleId = seed.PlainRole.Id, expectedCurrentRoleId = seed.ContentRole.Id }, ct),
            "bulk-assign" => _client.PostAsJsonAsync(
                "/api/v1/admin/role-assignments/actions/bulk-assign", new { roleId = seed.ContentRole.Id, ids = new[] { seed.TargetUserId }, allMatching = false }, ct),
            "bulk-assign-over-holder" => _client.PostAsJsonAsync(
                "/api/v1/admin/role-assignments/actions/bulk-assign", new { roleId = seed.PlainRole.Id, ids = new[] { seed.HolderUserId }, allMatching = false }, ct),
            "legacy-change-role" => _client.PatchAsJsonAsync(
                $"/api/v1/users/{seed.HolderUserId}/role", new { role = PrivilegedRoleNames.Regular }, ct),
            "administrator-switch" => _client.PutAsJsonAsync(
                "/api/v1/admin/roles/administrator/content-access", new { granted = !seed.AdministratorContentAccess }, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };
    }

    private async Task<Seed> SeedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        AuthenticateAsSuperUser();

        var contentRole = await CreateRoleAsync(UniqueRoleName("content"), [PlainKey, ContentView]);
        var plainRole = await CreateRoleAsync(UniqueRoleName("plain"), [PlainKey]);
        var targetUserId = await SeedUserAsync();
        var holderUserId = await SeedUserAsync();

        using (var assigned = await _client.PutAsJsonAsync(
            $"/api/v1/admin/role-assignments/{holderUserId}", new { roleId = contentRole.Id, expectedCurrentRoleId = (string?)null }, ct))
        {
            assigned.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var access = await _client.GetAsync("/api/v1/admin/roles/administrator/content-access", ct);
        access.StatusCode.Should().Be(HttpStatusCode.OK);
        using var accessJson = await ReadJsonAsync(access);

        return new Seed(contentRole, plainRole, targetUserId, holderUserId, accessJson.RootElement.GetProperty("granted").GetBoolean());
    }

    private async Task<RoleSnapshot> CreateRoleAsync(string name, string[] permissionKeys)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/roles", new { name, description = (string?)null, permissionKeys }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var json = await ReadJsonAsync(response);

        return new RoleSnapshot(json.RootElement.GetProperty("id").GetString()!, json.RootElement.GetProperty("concurrencyStamp").GetString()!);
    }

    private async Task<string> SeedUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"cpg-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };

        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<bool> HoldsContentRoleAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        return await db.UserRoles.AnyAsync(
            ur => ur.UserId == seed.HolderUserId && ur.RoleId == seed.ContentRole.Id, TestContext.Current.CancellationToken);
    }

    private async Task<int> CountWritesByAsync(string actorUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        return await db.RoleAuditLogs.CountAsync(
            a => a.ActorUserId == actorUserId && WriteActions.Contains(a.Action), TestContext.Current.CancellationToken);
    }

    private async Task CleanupAsync(Seed seed)
    {
        var ct = TestContext.Current.CancellationToken;

        // Restores the Administrator switch before anything else, so a failure below can't leave it flipped.
        AuthenticateAsSuperUser();
        using (var restored = await _client.PutAsJsonAsync(
            "/api/v1/admin/roles/administrator/content-access", new { granted = seed.AdministratorContentAccess }, ct))
        {
            restored.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        foreach (var userId in new[] { seed.TargetUserId, seed.HolderUserId })
        {
            if (await userManager.FindByIdAsync(userId) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }

        // The Super User's "create" path leaves a third role behind; it carries the same test prefix.
        var roles = await db.Roles.Where(r => r.Id == seed.ContentRole.Id || r.Id == seed.PlainRole.Id || r.Name!.StartsWith(RoleNamePrefix)).ToListAsync(ct);
        foreach (var role in roles)
        {
            await roleManager.DeleteAsync(role);
        }

        await db.RoleAuditLogs.Where(a => a.ActorUserId == _administratorId || a.ActorUserId == _superUserId).ExecuteDeleteAsync(ct);
    }

    private string RoleNamePrefix => $"Cpg {_superUserId[^8..]}";

    private string UniqueRoleName(string label) => $"{RoleNamePrefix} {label} {Guid.NewGuid().ToString("N")[..6]}";

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);
}
