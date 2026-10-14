using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Hub = AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>One real host and database for the preferences tests, so they share one rate-limit state and one set of seeded users.</summary>
public sealed class NotificationPreferencesApiFactory : CustomWebApplicationFactory;

/// <summary>
/// T136 — specs/067 US4, contracts/notifications-api.md `GET`/`PUT /users/me/notification-preferences`, over the real
/// host and database: the status codes (401, 400, 422, 429), the sparse storage, and what a saved preference does to
/// the routing of a later event.
/// </summary>
public sealed class NotificationPreferencesEndpointsTests(NotificationPreferencesApiFactory factory)
    : IClassFixture<NotificationPreferencesApiFactory>, IAsyncLifetime
{
    private const string Endpoint = "/api/v1/users/me/notification-preferences";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<string> _seededUserIds = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId != null && _seededUserIds.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        await db.NotificationOutboxEvents.Where(e => e.EventKey != null && e.EventKey.StartsWith("prefs-test:")).ExecuteDeleteAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in _seededUserIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    private async Task<string> SeedUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"prefs-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        _seededUserIds.Add(user.Id);
        return user.Id;
    }

    private HttpClient ClientFor(string? userId)
    {
        var client = factory.CreateClient();
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId));
        }

        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, object body) =>
        client.PutAsJsonAsync(Endpoint, body, JsonOptions, TestContext.Current.CancellationToken);

    private static async Task<NotificationPreferencesDto> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<NotificationPreferencesDto>(JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private static NotificationChannelPreferenceDto Pair(NotificationPreferencesDto dto, NotificationCategory category, NotificationChannel channel) =>
        dto.Categories.Single(c => c.Category == category).Channels.Single(c => c.Channel == channel);

    private static object Change(string category, string channel, bool enabled) => new { category, channel, enabled };

    // --- 401 ---

    [Fact]
    public async Task Get_Returns401_WithoutAToken()
    {
        var response = await ClientFor(null).GetAsync(Endpoint, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Put_Returns401_WithoutAToken()
    {
        var response = await PutAsync(ClientFor(null), new { changes = new[] { Change("Document", "Email", false) } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- GET / PUT ---

    [Fact]
    public async Task Get_ReturnsTheEffectiveDefaults_WithMandatoryPairsLocked()
    {
        var client = ClientFor(await SeedUserAsync());

        var dto = await ReadAsync(await client.GetAsync(Endpoint, TestContext.Current.CancellationToken));

        dto.Categories.Select(c => c.Category).Should().NotContain([NotificationCategory.Billing, NotificationCategory.Conversation]);
        Pair(dto, NotificationCategory.Security, NotificationChannel.Email).Should().Be(new NotificationChannelPreferenceDto(NotificationChannel.Email, true, true));
        Pair(dto, NotificationCategory.Document, NotificationChannel.InApp).Locked.Should().BeFalse();
    }

    [Fact]
    public async Task Put_SavesAnOverride_ReturnsTheNewState_AndAGetAgrees()
    {
        var userId = await SeedUserAsync();
        var client = ClientFor(userId);
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Document, NotificationChannel.Email);

        var dto = await ReadAsync(await PutAsync(client, new { changes = new[] { Change("Document", "Email", !defaultEmail) } }));

        Pair(dto, NotificationCategory.Document, NotificationChannel.Email).Enabled.Should().Be(!defaultEmail);
        var again = await ReadAsync(await client.GetAsync(Endpoint, TestContext.Current.CancellationToken));
        Pair(again, NotificationCategory.Document, NotificationChannel.Email).Enabled.Should().Be(!defaultEmail);
        (await OverrideRowsAsync(userId)).Should().Be(1);
    }

    [Fact]
    public async Task Put_BackToTheDefault_RemovesTheRow_AndCanBeSavedAgain()
    {
        var userId = await SeedUserAsync();
        var client = ClientFor(userId);
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Document, NotificationChannel.Email);

        await ReadAsync(await PutAsync(client, new { changes = new[] { Change("Document", "Email", !defaultEmail) } }));
        await ReadAsync(await PutAsync(client, new { changes = new[] { Change("Document", "Email", defaultEmail) } }));
        (await OverrideRowsAsync(userId)).Should().Be(0, "a value equal to the default is stored as no row");

        // A real delete, not a soft one: the unique index would otherwise refuse the pair a second time.
        await ReadAsync(await PutAsync(client, new { changes = new[] { Change("Document", "Email", !defaultEmail) } }));
        (await OverrideRowsAsync(userId)).Should().Be(1);
    }

    [Fact]
    public async Task Put_DoesNotTouchAnotherUsersPreferences()
    {
        var first = await SeedUserAsync();
        var second = await SeedUserAsync();
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Document, NotificationChannel.Email);

        await ReadAsync(await PutAsync(ClientFor(first), new { changes = new[] { Change("Document", "Email", !defaultEmail) } }));

        var other = await ReadAsync(await ClientFor(second).GetAsync(Endpoint, TestContext.Current.CancellationToken));
        Pair(other, NotificationCategory.Document, NotificationChannel.Email).Enabled.Should().Be(defaultEmail);
    }

    // --- 422: mandatory pairs ---

    [Fact]
    public async Task Put_DisablingAMandatoryPair_Returns422_WithTheChangeIndex_AndAppliesNothing()
    {
        var userId = await SeedUserAsync();
        var client = ClientFor(userId);
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Document, NotificationChannel.Email);

        var response = await PutAsync(client, new
        {
            changes = new[] { Change("Document", "Email", !defaultEmail), Change("Security", "Email", false) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var errors = body.RootElement.GetProperty("errors");
        errors.TryGetProperty("changes[1]", out _).Should().BeTrue();
        errors.TryGetProperty("changes[0]", out _).Should().BeFalse();
        (await OverrideRowsAsync(userId)).Should().Be(0, "the request is atomic, so the valid first change isn't applied either");
    }

    // --- 400: the shape ---

    [Fact]
    public async Task Put_WithNoChanges_Returns400()
    {
        var response = await PutAsync(ClientFor(await SeedUserAsync()), new { changes = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_WithMoreThanFortyChanges_Returns400()
    {
        var changes = Enumerable.Repeat(Change("Document", "Email", false), 41).ToArray();

        var response = await PutAsync(ClientFor(await SeedUserAsync()), new { changes });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_WithADigestFrequency_Returns400()
    {
        var response = await PutAsync(
            ClientFor(await SeedUserAsync()),
            new { changes = new[] { new { category = "Document", channel = "Email", enabled = false, frequency = "DailyDigest" } } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_WithAnUnknownCategory_Returns400()
    {
        var response = await PutAsync(ClientFor(await SeedUserAsync()), new { changes = new[] { Change("Nonsense", "Email", false) } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- 429 ---

    [Fact]
    public async Task Get_Returns429_AfterTheMinuteQuotaIsSpent()
    {
        // The limiter is partitioned per user, so a user of its own can spend the quota without touching any other test.
        var client = ClientFor(await SeedUserAsync());
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 125 && last != HttpStatusCode.TooManyRequests; i++)
        {
            last = (await client.GetAsync(Endpoint, TestContext.Current.CancellationToken)).StatusCode;
        }

        last.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // --- routing ---

    [Fact]
    public async Task ASavedPreference_SkipsThatChannelForLaterEvents_ButNotForOtherUsers_AndInAppStillDelivers()
    {
        var optedOut = await SeedUserAsync();
        var untouched = await SeedUserAsync();
        await ReadAsync(await PutAsync(ClientFor(optedOut), new { changes = new[] { Change("Document", "Email", false) } }));

        var key = $"prefs-test:{Guid.NewGuid():N}";
        await PublishStorageLimitAsync(optedOut, key);
        await PublishStorageLimitAsync(untouched, key);

        var optedOutDeliveries = await WaitForDeliveriesAsync(optedOut);
        var untouchedDeliveries = await WaitForDeliveriesAsync(untouched);

        var email = optedOutDeliveries.Single(d => d.Channel == NotificationChannel.Email);
        email.Status.Should().Be(DeliveryStatus.Skipped);
        email.SkipReason.Should().Be(DeliverySkipReason.PreferenceDisabled);
        optedOutDeliveries.Single(d => d.Channel == NotificationChannel.InApp).Status.Should().Be(DeliveryStatus.Delivered);

        untouchedDeliveries.Single(d => d.Channel == NotificationChannel.Email).Status.Should().NotBe(DeliveryStatus.Skipped);
    }

    private async Task PublishStorageLimitAsync(string userId, string key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<Hub.INotificationPublisher>();
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.DocumentStorageLimitReached,
            new NotificationRecipient.User(userId),
            new Dictionary<string, string?> { ["usedStorage"] = "9.8 GB", ["storageLimit"] = "10 GB" },
            EventKey: key));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Waits for the host's own dispatcher to materialize the user's notification with both of its deliveries.</summary>
    private async Task<List<NotificationDelivery>> WaitForDeliveriesAsync(string userId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
            var notification = await db.Notifications.IgnoreQueryFilters().Include(n => n.Deliveries)
                .FirstOrDefaultAsync(n => n.RecipientUserId == userId, TestContext.Current.CancellationToken);
            if (notification is { Deliveries.Count: >= 2 })
            {
                return [.. notification.Deliveries];
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No notification with its deliveries appeared for {userId} within 30 s.");
    }

    private async Task<int> OverrideRowsAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        return await db.NotificationPreferences.IgnoreQueryFilters().CountAsync(p => p.UserId == userId, TestContext.Current.CancellationToken);
    }
}
