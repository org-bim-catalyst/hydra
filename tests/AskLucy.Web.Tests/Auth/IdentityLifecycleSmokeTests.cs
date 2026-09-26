using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Application.Users;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>Captures outgoing mail so a test can follow the confirmation link registration sends.</summary>
public sealed class CapturingEmailWebApplicationFactory : CustomWebApplicationFactory
{
    public ConcurrentQueue<(string To, string TextBody)> SentEmails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new CapturingEmailSender(SentEmails));
        });
    }

    private sealed class CapturingEmailSender(ConcurrentQueue<(string To, string TextBody)> sent) : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
        {
            sent.Enqueue((toEmail, textBody));
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// specs/074 — guards the RepairIdentityClaimsTables migration end to end: the Identity tables it
/// rebuilt (AspNetRoleClaims, AspNetUserClaims) sit under registration, sign-in, custom roles and
/// their permissions, so this walks one real account through all of them against the real schema.
/// </summary>
public sealed partial class IdentityLifecycleSmokeTests(CapturingEmailWebApplicationFactory factory)
    : IClassFixture<CapturingEmailWebApplicationFactory>
{
    private const string Password = "Smoke-Passw0rd!";
    private const string DashboardSummary = "/api/v1/admin/dashboard/summary";
    private const string DashboardView = "admin.dashboard.view";
    private const string UsersView = "admin.users.view";

    private readonly string _email = $"smoke-{Guid.NewGuid():N}@tests.asklucy.io";
    private readonly string _superUserId = $"smoke-super-{Guid.NewGuid():N}";

    [Fact]
    public async Task Register_SignIn_Roles_Permissions_Claims_AndDelete_AllWorkOnTheRepairedSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(_superUserId, PrivilegedRoleNames.SuperUser));

        string? userId = null;
        var roleIds = new List<string>();
        try
        {
            // Register, then sign-in is refused until the emailed link is followed.
            using (var registered = await user.PostAsJsonAsync(
                "/api/v1/auth/register", new { email = _email, password = Password, firstName = "Smoke", lastName = "Test" }, ct))
            {
                registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync(ct));
            }

            using (var early = await LoginAsync(user))
            {
                early.StatusCode.Should().Be(HttpStatusCode.Forbidden, "an unconfirmed email cannot sign in");
            }

            var link = ConfirmationLink().Match(factory.SentEmails.Single(m => m.To == _email).TextBody);
            link.Success.Should().BeTrue("registration emails a confirmation link");
            userId = Uri.UnescapeDataString(link.Groups["userId"].Value);

            using (var confirmed = await user.PostAsJsonAsync(
                "/api/v1/auth/confirm-email", new { userId, token = Uri.UnescapeDataString(link.Groups["token"].Value) }, ct))
            {
                confirmed.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }

            // Sign in, then the refresh cookie rotates.
            using (var login = await LoginAsync(user))
            {
                login.StatusCode.Should().Be(HttpStatusCode.OK);
                await UseAccessTokenAsync(user, login);
            }

            await RefreshAsync(user);
            (await SessionPermissionsAsync(user)).Should().NotContain(DashboardView);
            (await GetStatusAsync(user, DashboardSummary)).Should().Be(HttpStatusCode.Forbidden);

            // A custom role with permissions — the write that used to 500 on AspNetRoleClaims.
            var dashboardRole = await CreateRoleAsync(admin, [DashboardView]);
            var usersRole = await CreateRoleAsync(admin, [UsersView]);
            roleIds.AddRange([dashboardRole.Id, usersRole.Id]);
            (await RoleClaimValuesAsync(dashboardRole.Id)).Should().Equal(DashboardView);

            // A new account starts on the built-in User role, never with no role.
            var userRoleId = await CurrentRoleIdAsync(userId);
            (await RoleNameAsync(userRoleId!)).Should().Be(DefaultRole.Name);

            // Assigning it reaches the user's session and the permission-gated endpoint.
            await AssignAsync(admin, userId, dashboardRole.Id, expectedCurrentRoleId: userRoleId);
            await RefreshAsync(user);
            (await SessionPermissionsAsync(user)).Should().Contain(DashboardView);
            (await GetStatusAsync(user, DashboardSummary)).Should().Be(HttpStatusCode.OK);

            // Replacing it takes the old permission away and grants the new one.
            await AssignAsync(admin, userId, usersRole.Id, expectedCurrentRoleId: dashboardRole.Id);
            await RefreshAsync(user);
            var afterReplace = await SessionPermissionsAsync(user);
            afterReplace.Should().Contain(UsersView).And.NotContain(DashboardView);
            (await GetStatusAsync(user, DashboardSummary)).Should().Be(HttpStatusCode.Forbidden);

            // Editing the role's permissions rewrites its claims.
            using (var updated = await admin.PutAsJsonAsync(
                $"/api/v1/admin/roles/{usersRole.Id}",
                new { name = usersRole.Name, description = (string?)null, permissionKeys = new[] { UsersView, DashboardView }, concurrencyStamp = usersRole.ConcurrencyStamp }, ct))
            {
                updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(ct));
            }

            (await RoleClaimValuesAsync(usersRole.Id)).Should().BeEquivalentTo([UsersView, DashboardView]);

            // A Super User can save any role under a new name, permissions and all.
            var copyName = $"{usersRole.Name} copy";
            using (var duplicated = await admin.PostAsJsonAsync(
                $"/api/v1/admin/roles/{usersRole.Id}/duplicate", new { name = copyName, description = (string?)null }, ct))
            {
                duplicated.StatusCode.Should().Be(HttpStatusCode.Created, await duplicated.Content.ReadAsStringAsync(ct));
                using var json = await ReadJsonAsync(duplicated);
                var copyId = json.RootElement.GetProperty("id").GetString()!;
                roleIds.Add(copyId);
                json.RootElement.GetProperty("name").GetString().Should().Be(copyName);
                (await RoleClaimValuesAsync(copyId)).Should().BeEquivalentTo([UsersView, DashboardView]);
            }

            await RefreshAsync(user);
            (await SessionPermissionsAsync(user)).Should().Contain([UsersView, DashboardView]);

            // User claims — the other rebuilt table.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var account = (await userManager.FindByIdAsync(userId))!;
                (await userManager.AddClaimAsync(account, new Claim("smoke", "one"))).Succeeded.Should().BeTrue();
                (await userManager.AddClaimAsync(account, new Claim("smoke", "two"))).Succeeded.Should().BeTrue();
                (await userManager.GetClaimsAsync(account)).Where(c => c.Type == "smoke").Select(c => c.Value).Should().BeEquivalentTo(["one", "two"]);
                (await userManager.RemoveClaimAsync(account, new Claim("smoke", "one"))).Succeeded.Should().BeTrue();
                (await userManager.GetClaimsAsync(account)).Where(c => c.Type == "smoke").Select(c => c.Value).Should().Equal("two");
            }

            // Deleting a role takes its claims with it.
            // Deleting a held role moves its holder to the User role.
            await AssignAsync(admin, userId, dashboardRole.Id, expectedCurrentRoleId: usersRole.Id);
            var current = await GetRoleAsync(admin, dashboardRole.Id);
            using (var deleted = await admin.DeleteAsync(
                $"/api/v1/admin/roles/{dashboardRole.Id}?concurrencyStamp={Uri.EscapeDataString(current.ConcurrencyStamp)}", ct))
            {
                deleted.IsSuccessStatusCode.Should().BeTrue(await deleted.Content.ReadAsStringAsync(ct));
            }

            (await RoleClaimValuesAsync(dashboardRole.Id)).Should().BeEmpty();
            (await CurrentRoleIdAsync(userId)).Should().Be(userRoleId);

            // Deleting the account takes its claims with it.
            await DeleteUserAsync(userId);
            (await CountUserClaimsAsync(userId)).Should().Be(0);
            userId = null;
        }
        finally
        {
            await CleanupAsync(userId, roleIds);
        }
    }

    private sealed record RoleSnapshot(string Id, string Name, string ConcurrencyStamp);

    [GeneratedRegex(@"userId=(?<userId>[^&\s""<]+)&token=(?<token>[^\s""<]+)")]
    private static partial Regex ConfirmationLink();

    private Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email = _email, password = Password }, TestContext.Current.CancellationToken);

    private static async Task RefreshAsync(HttpClient client)
    {
        using var refreshed = await client.PostAsync("/api/v1/auth/refresh", content: null, TestContext.Current.CancellationToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        await UseAccessTokenAsync(client, refreshed);
    }

    private static async Task UseAccessTokenAsync(HttpClient client, HttpResponseMessage response)
    {
        using var json = await ReadJsonAsync(response);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
    }

    private static async Task<string[]> SessionPermissionsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);

        return [.. json.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];
    }

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private async Task<RoleSnapshot> CreateRoleAsync(HttpClient admin, string[] permissionKeys)
    {
        var name = $"Smoke {_superUserId[^8..]} {Guid.NewGuid().ToString("N")[..6]}";
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/roles", new { name, description = (string?)null, permissionKeys }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var json = await ReadJsonAsync(response);

        return new RoleSnapshot(json.RootElement.GetProperty("id").GetString()!, name, json.RootElement.GetProperty("concurrencyStamp").GetString()!);
    }

    private static async Task<RoleSnapshot> GetRoleAsync(HttpClient admin, string roleId)
    {
        using var response = await admin.GetAsync($"/api/v1/admin/roles/{roleId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);

        return new RoleSnapshot(roleId, json.RootElement.GetProperty("name").GetString()!, json.RootElement.GetProperty("concurrencyStamp").GetString()!);
    }

    private static async Task AssignAsync(HttpClient admin, string userId, string roleId, string? expectedCurrentRoleId)
    {
        using var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/role-assignments/{userId}", new { roleId, expectedCurrentRoleId }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task<string[]> RoleClaimValuesAsync(string roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        return await db.RoleClaims.Where(c => c.RoleId == roleId).Select(c => c.ClaimValue!).ToArrayAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> CurrentRoleIdAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        return await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> RoleNameAsync(string roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        return await db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountUserClaimsAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        return await db.UserClaims.CountAsync(c => c.UserId == userId, TestContext.Current.CancellationToken);
    }

    private async Task DeleteUserAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByIdAsync(userId) is { } account)
        {
            (await userManager.DeleteAsync(account)).Succeeded.Should().BeTrue();
        }
    }

    private async Task CleanupAsync(string? userId, List<string> roleIds)
    {
        if (userId is not null)
        {
            await DeleteUserAsync(userId);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        foreach (var roleId in roleIds)
        {
            if (await roleManager.FindByIdAsync(roleId) is { } role)
            {
                await roleManager.DeleteAsync(role);
            }
        }

        await db.RoleAuditLogs.Where(a => a.ActorUserId == _superUserId).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);
}
