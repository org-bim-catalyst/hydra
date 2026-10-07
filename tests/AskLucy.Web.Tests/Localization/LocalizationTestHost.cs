using System.Net.Http.Headers;
using AskLucy.Application.Localization;
using AskLucy.Domain.Localization;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Web.Tests.Localization;

/// <summary>
/// Shared plumbing for the localization tests (specs/067 US8). The platform setting is one global row, so every test that changes it puts it
/// back to "disabled, English only" when it finishes, and evicts the cache so the next test sees the database.
/// </summary>
public sealed class LocalizationTestHost(CustomWebApplicationFactory factory) : IAsyncDisposable
{
    private readonly List<string> _userIds = [];
    private readonly List<string> _actors = [];

    public HttpClient AdminClient(params string[] permissions)
    {
        var id = $"loc-admin-{Guid.NewGuid():N}";
        _actors.Add(id);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(id, [], permissions));
        return client;
    }

    public string LastAdminId => _actors[^1];

    /// <summary>A real account (the choice is stored on it), optionally already preferring a language.</summary>
    public async Task<(HttpClient Client, string UserId)> UserAsync(string? preferredLanguage = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"loc-user-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow,
            PreferredLanguage = preferredLanguage,
        };
        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        _userIds.Add(user.Id);
        _actors.Add(user.Id);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(user.Id));
        return (client, user.Id);
    }

    /// <summary>Changes the platform setting directly, as an administrator's save would.</summary>
    public async Task SetPlatformAsync(bool enabled, params string[] languages)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var setting = await db.LocalizationSettings.SingleAsync(s => s.Id == LocalizationSetting.SingletonId);
        setting.Update(enabled, languages);
        await db.SaveChangesAsync();
        factory.Services.GetRequiredService<ILocalizationSettingsProvider>().Evict();
    }

    public async ValueTask DisposeAsync()
    {
        await SetPlatformAsync(false, PlatformLanguages.English);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.NotificationAuditLogs.IgnoreQueryFilters().Where(a => a.ActorUserId != null && _actors.Contains(a.ActorUserId)).ExecuteDeleteAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in _userIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }
}
