using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>One host and database for the admin notification tests, with a mail password and support address to prove never leak.</summary>
public sealed class AdminNotificationsFactory : CustomWebApplicationFactory
{
    public const string MailPassword = "s3cret-mail-password-for-tests";
    public const string SupportAddress = "support-mailbox-for-tests@secret.example";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Password"] = MailPassword,
            ["Smtp:Username"] = "mail-user-for-tests",
            ["Smtp:FromSupport"] = SupportAddress,
        }));
    }
}

/// <summary>
/// T151 — specs/067 US6, contracts/admin-notifications-api.md over the real host and database: the permission matrix (none is 403,
/// view reads and can't act, manage does both), what a response never contains, and the once-an-hour <c>…Viewed</c> audit.
/// </summary>
public sealed class AdminNotificationsEndpointsTests(AdminNotificationsFactory factory) : IClassFixture<AdminNotificationsFactory>, IAsyncLifetime
{
    private const string Base = "/api/v1/admin/notifications";
    private const string View = "admin.notifications.view";
    private const string Manage = "admin.notifications.manage";

    private readonly List<string> _actors = [];
    private readonly List<Guid> _notificationIds = [];
    private readonly List<string> _userIds = [];
    private readonly List<string> _roleIds = [];
    private string _leakEmail = string.Empty;
    private Guid _failedDeliveryId;

    public async ValueTask InitializeAsync()
    {
        _leakEmail = $"leak-{Guid.NewGuid():N}@tests.asklucy.io";
        await SeedFailedDeliveriesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.Notifications.IgnoreQueryFilters().Where(n => _notificationIds.Contains(n.Id)).ExecuteDeleteAsync();
        await db.NotificationAuditLogs.IgnoreQueryFilters().Where(a => a.ActorUserId != null && _actors.Contains(a.ActorUserId)).ExecuteDeleteAsync();
        await db.NotificationOutboxEvents.Where(e => e.EventKey != null && e.EventKey.StartsWith("announcement:") && e.RelatedItemId != null
            && db.SystemAnnouncements.Any(a => a.PublishedByUserId != null && _actors.Contains(a.PublishedByUserId) && e.RelatedItemId == a.Id.ToString())).ExecuteDeleteAsync();
        await db.SystemAnnouncements.IgnoreQueryFilters().Where(a => _actors.Contains(a.PublishedByUserId)).ExecuteDeleteAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in _userIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var id in _roleIds)
        {
            if (await roleManager.FindByIdAsync(id) is { } role)
            {
                await roleManager.DeleteAsync(role);
            }
        }
    }

    private HttpClient ClientFor(string? userId, params string[] permissions)
    {
        var client = factory.CreateClient();
        if (userId is not null)
        {
            _actors.Add(userId);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId, [], permissions));
        }

        return client;
    }

    private async Task<string> SeedUserAsync(bool administrator = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"announcer-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        if (administrator)
        {
            // Permissions of a real account are read from the database, not from the token, so the role is real.
            (await userManager.AddToRoleAsync(user, "Administrator")).Succeeded.Should().BeTrue();
        }

        _userIds.Add(user.Id);
        _actors.Add(user.Id);
        return user.Id;
    }

    private static string NewAdminId() => $"admin-{Guid.NewGuid():N}";

    /// <summary>A user-addressed failed email, an address-addressed failed email and a failed support-mailbox email.</summary>
    private async Task SeedFailedDeliveriesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = _leakEmail, Email = _leakEmail, FirstName = "Layla", LastName = "Hassan", EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        _userIds.Add(user.Id);

        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var now = DateTime.UtcNow;
        var corr = $"corr-{Guid.NewGuid():N}";

        var failedUserMail = Notification.Create(
            user.Id, NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed), NotificationPriority.High,
            "Workflow failed", "Message", "en", corr, now, showInCenter: false);
        var userDelivery = NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.High, RecipientKind.User, null, 5, null, corr, now);
        failedUserMail.AddDelivery(userDelivery);
        userDelivery.MarkSending("test-worker", now.AddMinutes(2), now);
        userDelivery.Fail(DeliveryFailureKind.Permanent, "Mail server rejected the message: mailbox unavailable.", "550 5.1.1", now);
        _failedDeliveryId = userDelivery.Id;

        var accountMail = Notification.Create(
            user.Id, NotificationTypeCatalog.Get(NotificationTypeKeys.AccountPasswordResetRequested), NotificationPriority.Critical,
            string.Empty, string.Empty, "en", corr, now, showInCenter: false);
        var accountDelivery = NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Critical, RecipientKind.Address, _leakEmail, 5, null, corr, now);
        accountMail.AddDelivery(accountDelivery);
        accountDelivery.MarkSending("test-worker", now.AddMinutes(2), now);
        accountDelivery.Fail(DeliveryFailureKind.Permanent, "Mail server rejected the message: mailbox unavailable.", "550 5.1.1", now);

        var supportMail = Notification.Create(
            null, NotificationTypeCatalog.Get(NotificationTypeKeys.AccountSupportRequestSubmitted), NotificationPriority.Normal,
            string.Empty, string.Empty, "en", corr, now, showInCenter: false);
        var supportDelivery = NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Normal, RecipientKind.SupportMailbox, null, 5, null, corr, now);
        supportMail.AddDelivery(supportDelivery);
        supportDelivery.MarkSending("test-worker", now.AddMinutes(2), now);
        supportDelivery.Fail(DeliveryFailureKind.Permanent, "Mail server rejected the message: mailbox unavailable.", "550 5.1.1", now);

        db.Notifications.AddRange(failedUserMail, accountMail, supportMail);
        await db.SaveChangesAsync();
        _notificationIds.AddRange([failedUserMail.Id, accountMail.Id, supportMail.Id]);
    }

    // ---- 401 and the permission matrix ----

    public static TheoryData<string, string> Reads() => new()
    {
        { "GET", "/statistics" },
        { "GET", "/channels" },
        { "GET", "/deliveries" },
        { "GET", "/deliveries/00000000-0000-0000-0000-000000000001" },
        { "GET", "/audit" },
        { "GET", "/announcements" },
    };

    public static TheoryData<string, string> Actions() => new()
    {
        { "POST", "/deliveries/00000000-0000-0000-0000-000000000001/actions/retry" },
        { "POST", "/deliveries/actions/retry" },
        { "POST", "/announcements" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), Base + path)
    {
        Content = method == "POST" ? JsonContent.Create(BodyFor(path)) : null,
    };

    private static object BodyFor(string path) => path switch
    {
        "/deliveries/actions/retry" => new { deliveryIds = new[] { Guid.NewGuid() } },
        "/announcements" => new { kind = "Maintenance", title = "Maintenance", message = "Down for a bit.", audience = "AllActiveUsers", isCritical = false },
        _ => new { },
    };

    [Theory]
    [MemberData(nameof(Reads))]
    [MemberData(nameof(Actions))]
    public async Task SignedOut_Gets401(string method, string path) =>
        (await ClientFor(null).SendAsync(Request(method, path), TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Theory]
    [MemberData(nameof(Reads))]
    [MemberData(nameof(Actions))]
    public async Task WithNoPermission_EverythingIs403(string method, string path) =>
        (await ClientFor(NewAdminId()).SendAsync(Request(method, path), TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task ViewReads_ButManageAloneDoesNotRead(string method, string path)
    {
        var viewer = await ClientFor(NewAdminId(), View).SendAsync(Request(method, path), TestContext.Current.CancellationToken);
        viewer.StatusCode.Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);

        // The server checks each endpoint on its own: manage doesn't quietly include view.
        var manager = await ClientFor(NewAdminId(), Manage).SendAsync(Request(method, path), TestContext.Current.CancellationToken);
        manager.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task ViewOnly_CantAct(string method, string path) =>
        (await ClientFor(NewAdminId(), View).SendAsync(Request(method, path), TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Manage_CanActOnDeliveries_AndGetsTheContractStatuses()
    {
        var admin = ClientFor(NewAdminId(), Manage, View);

        // A delivery that does not exist is a 404, not a quiet success.
        (await admin.PostAsync($"{Base}/deliveries/{Guid.NewGuid()}/actions/retry", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // An id with nothing behind it is reported as skipped, with the counts.
        var bulk = await admin.PostAsJsonAsync($"{Base}/deliveries/actions/retry", new { deliveryIds = new[] { Guid.NewGuid() } }, TestContext.Current.CancellationToken);
        bulk.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await bulk.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("requested").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("retried").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("skipped").GetArrayLength().Should().Be(1);

        // Neither of 'deliveryIds' nor 'filter' is a 400.
        (await admin.PostAsJsonAsync($"{Base}/deliveries/actions/retry", new { }, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Manage_RetriesAFailedDelivery_Audited_AndAnUnretryableOneIsA409WithAReason()
    {
        var adminId = NewAdminId();
        var admin = ClientFor(adminId, Manage, View);

        var first = await admin.PostAsync($"{Base}/deliveries/{_failedDeliveryId}/actions/retry", null, TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Pending");

        // The host's own delivery worker may already have picked it up; either way it is no longer failed, so it can't be retried again.
        var second = await admin.PostAsync($"{Base}/deliveries/{_failedDeliveryId}/actions/retry", null, TestContext.Current.CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("reason").GetString().Should().Be("NotFailed");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var rows = await db.NotificationAuditLogs.IgnoreQueryFilters()
            .Where(a => a.ActorUserId == adminId && a.Action == NotificationAuditAction.DeliveryRetried).ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().ContainSingle().Which.TargetId.Should().Be(_failedDeliveryId.ToString());
    }

    // ---- what a response never contains ----

    [Fact]
    public async Task NoResponse_ContainsACredential_TheSupportAddress_OrAnUnmaskedRecipientAddress()
    {
        var admin = ClientFor(NewAdminId(), View);
        var bodies = new List<string>();
        foreach (var path in new[] { "/statistics", "/channels", "/deliveries?limit=200", $"/deliveries/{_failedDeliveryId}", "/audit?limit=200", "/announcements" })
        {
            var response = await admin.GetAsync(Base + path, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
            bodies.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        var all = string.Join('\n', bodies);
        all.Should().NotContain(AdminNotificationsFactory.MailPassword);
        all.Should().NotContain("mail-user-for-tests");
        all.Should().NotContain(AdminNotificationsFactory.SupportAddress);
        all.Should().NotContain(_leakEmail, "recipient addresses are masked");
        all.Should().NotContain("test-smtp.invalid", "the mail host is not part of the channel summary");
    }

    [Fact]
    public async Task ADeliveryList_ShowsAMaskedAddress_InitialsForTheName_AndNothingForTheSupportMailbox()
    {
        var response = await ClientFor(NewAdminId(), View).GetAsync($"{Base}/deliveries?limit=200", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();

        var user = items.Single(i => i.GetProperty("deliveryId").GetGuid() == _failedDeliveryId);
        var recipient = user.GetProperty("recipient");
        recipient.GetProperty("address").GetString().Should().Be($"{_leakEmail[0]}•••{_leakEmail[_leakEmail.IndexOf('@')..]}");
        recipient.GetProperty("displayName").GetString().Should().Be("L. H.");
        user.GetProperty("retryable").GetBoolean().Should().BeTrue();
        user.GetProperty("notRetryableReason").ValueKind.Should().Be(JsonValueKind.Null);

        var support = items.Where(i => i.GetProperty("type").GetString() == NotificationTypeKeys.AccountSupportRequestSubmitted
            && i.GetProperty("correlationId").GetString() == user.GetProperty("correlationId").GetString()).ToList();
        support.Should().ContainSingle().Which.GetProperty("recipient").GetProperty("address").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ADeliveryDetail_OmitsTheTitle_ForSecurityAndAccountNotifications()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var accountDelivery = await db.Notifications.IgnoreQueryFilters().Include(n => n.Deliveries)
            .Where(n => _notificationIds.Contains(n.Id) && n.Type == NotificationTypeKeys.AccountPasswordResetRequested)
            .SelectMany(n => n.Deliveries).Select(d => d.Id).SingleAsync(TestContext.Current.CancellationToken);

        var response = await ClientFor(NewAdminId(), View).GetAsync($"{Base}/deliveries/{accountDelivery}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("notification").GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheChannelSummary_SaysWhatHealthIsAndNothingElse()
    {
        var response = await ClientFor(NewAdminId(), View).GetAsync($"{Base}/channels", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var channels = body.RootElement.EnumerateArray().ToDictionary(c => c.GetProperty("channel").GetString()!);
        channels.Keys.Should().BeEquivalentTo(["Email", "InApp"]);
        channels["Email"].GetProperty("provider").GetString().Should().Be("SMTP");
        channels["Email"].GetProperty("health").GetString().Should().BeOneOf("Healthy", "Degraded", "Unhealthy");
        channels["Email"].GetProperty("sendLimitPerMinute").GetInt32().Should().BeGreaterThan(0);
    }

    // ---- query validation ----

    [Fact]
    public async Task Statistics_RefuseARangeOverNinetyDays()
    {
        var to = DateTime.UtcNow;
        var response = await ClientFor(NewAdminId(), View).GetAsync(
            $"{Base}/statistics?from={Uri.EscapeDataString(to.AddDays(-91).ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deliveries_LimitIsCappedAtTwoHundred()
    {
        var response = await ClientFor(NewAdminId(), View).GetAsync($"{Base}/deliveries?limit=201", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- the audit of looking ----

    [Fact]
    public async Task ViewingDeliveries_IsAuditedOncePerAdminPerHour_ByTheBehaviour()
    {
        var adminId = NewAdminId();
        var admin = ClientFor(adminId, View);

        for (var i = 0; i < 3; i++)
        {
            (await admin.GetAsync($"{Base}/deliveries", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.NotificationAuditLogs.IgnoreQueryFilters().CountAsync(
            a => a.ActorUserId == adminId && a.Action == NotificationAuditAction.DeliveriesViewed, TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task ViewingOneDelivery_IsAuditedPerDelivery_AndDifferentResourcesAreAuditedSeparately()
    {
        var adminId = NewAdminId();
        var admin = ClientFor(adminId, View);

        await admin.GetAsync($"{Base}/deliveries/{_failedDeliveryId}", TestContext.Current.CancellationToken);
        await admin.GetAsync($"{Base}/deliveries/{_failedDeliveryId}", TestContext.Current.CancellationToken);
        await admin.GetAsync($"{Base}/statistics", TestContext.Current.CancellationToken);
        await admin.GetAsync($"{Base}/channels", TestContext.Current.CancellationToken);
        await admin.GetAsync($"{Base}/audit", TestContext.Current.CancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var actions = await db.NotificationAuditLogs.IgnoreQueryFilters().Where(a => a.ActorUserId == adminId)
            .Select(a => a.Action).ToListAsync(TestContext.Current.CancellationToken);
        actions.Should().BeEquivalentTo(
            [NotificationAuditAction.DeliveryViewed, NotificationAuditAction.StatisticsViewed, NotificationAuditAction.ChannelsViewed, NotificationAuditAction.AuditViewed]);
    }

    [Fact]
    public async Task ARefusedRead_WritesNoViewedRow()
    {
        var adminId = NewAdminId();

        (await ClientFor(adminId).GetAsync($"{Base}/deliveries", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.NotificationAuditLogs.IgnoreQueryFilters().AnyAsync(a => a.ActorUserId == adminId, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    // ---- announcements ----

    [Fact]
    public async Task APublishedAnnouncement_ReturnsTheEstimates_IsAuditedAndListed()
    {
        // A role nobody holds, so the fan-out reaches no one in the shared database.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var role = new ApplicationRole($"announce-test-{Guid.NewGuid():N}");
            (await scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>().CreateAsync(role)).Succeeded.Should().BeTrue();
            _roleIds.Add(role.Id);
        }

        // The publisher is recorded against a real account (a foreign key), so this admin is seeded.
        var adminId = await SeedUserAsync(administrator: true);
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(adminId, ["Administrator"], []));
        var response = await admin.PostAsJsonAsync(
            $"{Base}/announcements",
            new { kind = "Maintenance", title = "Scheduled maintenance", message = "Ask Lucy will be unavailable.", audience = "Roles", targetRoleIds = new[] { _roleIds[0] }, isCritical = true, endsAtUtc = DateTime.UtcNow.AddDays(1) },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        created.RootElement.GetProperty("estimatedRecipients").GetInt32().Should().Be(0);
        created.RootElement.GetProperty("emailEstimatedMinutes").GetInt32().Should().Be(0);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var list = await admin.GetAsync($"{Base}/announcements", TestContext.Current.CancellationToken);
        (await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(id.ToString());

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.NotificationAuditLogs.IgnoreQueryFilters().CountAsync(
            a => a.ActorUserId == adminId && a.Action == NotificationAuditAction.AnnouncementPublished && a.TargetId == id.ToString(), TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData("<b>Bold</b>")]
    [InlineData("Read https://example.com")]
    public async Task AnAnnouncementWithHtmlOrALink_IsRefused(string text)
    {
        var response = await ClientFor(NewAdminId(), Manage).PostAsJsonAsync(
            $"{Base}/announcements",
            new { kind = "Maintenance", title = "Maintenance", message = text, audience = "AllActiveUsers", isCritical = false },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnAnnouncement_CantBeEditedOrDeleted()
    {
        var admin = ClientFor(NewAdminId(), Manage, View);
        var id = Guid.NewGuid();

        (await admin.PutAsJsonAsync($"{Base}/announcements/{id}", new { }, TestContext.Current.CancellationToken)).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await admin.DeleteAsync($"{Base}/announcements/{id}", TestContext.Current.CancellationToken)).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }
}
