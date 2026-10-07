using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// T174 — specs/067 US7, contracts/admin-notifications-api.md template endpoints over the real host and database: preview through the
/// production renderer, the test send, If-Match concurrency, validation (422), and the audit trail. Each test owns a template in its own
/// language, so it never touches the shipped defaults.
/// </summary>
public sealed class NotificationTemplateEndpointsTests(AdminNotificationsFactory factory) : IClassFixture<AdminNotificationsFactory>, IAsyncLifetime
{
    private const string Base = "/api/v1/admin/notifications/templates";
    private const string Subject = "{{ workflowName }} failed";

    private readonly string _language = $"t{Guid.NewGuid().ToString("N")[..8]}";
    private readonly List<string> _actors = [];
    private readonly List<string> _userIds = [];
    private Guid _templateId;
    private Guid _publishedId;
    private Guid _draftId;

    public async ValueTask InitializeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var now = DateTime.UtcNow;
        var template = NotificationTemplate.Create(NotificationTypeKeys.WorkflowExecutionFailed, NotificationChannel.Email, _language, "Endpoint test", now);
        var v1 = template.AddDraft(Content("Published {{ workflowName }}"), now);
        var v2 = template.AddDraft(Content("Draft {{ workflowName }}"), now);
        db.NotificationTemplates.Add(template);
        await db.SaveChangesAsync();
        template.Publish(v1.Id, "test", now);
        await db.SaveChangesAsync();

        _templateId = template.Id;
        _publishedId = v1.Id;
        _draftId = v2.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.NotificationOutboxEvents.Where(e => e.Type == NotificationTypeKeys.TemplateTest && _userIds.Any(id => e.RecipientJson.Contains(id))).ExecuteDeleteAsync();
        await db.NotificationAuditLogs.IgnoreQueryFilters().Where(a => a.ActorUserId != null && _actors.Contains(a.ActorUserId)).ExecuteDeleteAsync();
        await db.NotificationTemplates.IgnoreQueryFilters().Where(t => t.Language == _language).ExecuteDeleteAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in _userIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    private static NotificationTemplateContent Content(string heading) => new()
    {
        Subject = Subject,
        Heading = heading,
        BodyParagraphs = ["It failed: {{ failureSummary }}"],
        SafetyNote = "Ignore this if it wasn't you.",
        ActionLabel = "Open",
    };

    private static object Body(string heading = "Edited {{ workflowName }}", string subject = Subject) => new
    {
        subject,
        heading,
        bodyParagraphs = new[] { "It failed: {{ failureSummary }}" },
        safetyNote = "Ignore this if it wasn't you.",
        actionLabel = "Open",
    };

    private HttpClient Client(string userId, params string[] permissions)
    {
        _actors.Add(userId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId, [], permissions));
        return client;
    }

    private HttpClient Manager() => Client($"tpl-{Guid.NewGuid():N}", "admin.notifications.view", "admin.notifications.manage");

    /// <summary>A real, verified administrator: permissions of a real account come from the database, so the role is real.</summary>
    private async Task<(HttpClient Client, string UserId)> RealAdminAsync(bool verified = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"tpl-admin-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Ada", LastName = "Lovelace", EmailConfirmed = verified, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, "Administrator")).Succeeded.Should().BeTrue();
        _userIds.Add(user.Id);
        _actors.Add(user.Id);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(user.Id, ["Administrator"], []));
        return (client, user.Id);
    }

    /// <summary>The row version is base64 (it can hold '+', '/' and '='), which isn't an HTTP entity tag, so the header is added unvalidated.</summary>
    private static HttpRequestMessage WithMatch(HttpRequestMessage request, string rowVersion)
    {
        request.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        return request;
    }

    private static HttpRequestMessage Put(string url, object body, string rowVersion) =>
        WithMatch(new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) }, rowVersion);

    private static HttpRequestMessage PostWithMatch(string url, string rowVersion) =>
        WithMatch(new HttpRequestMessage(HttpMethod.Post, url), rowVersion);

    private async Task<string> RowVersionAsync(HttpClient client, Guid versionId)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"{Base}/{_templateId}/versions/{versionId}", TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task<JsonDocument> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    // ---- permissions ----

    [Fact]
    public async Task Permissions_ViewReadsAndPreviews_ButCantChange()
    {
        var viewer = Client($"tpl-{Guid.NewGuid():N}", "admin.notifications.view");
        var ct = TestContext.Current.CancellationToken;

        (await viewer.GetAsync($"{Base}?language={_language}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync($"{Base}/{_templateId}/versions/{_draftId}/actions/preview", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync($"{Base}/{_templateId}/versions", Body(), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"{Base}/{_templateId}/versions/{_draftId}/actions/send-test", null, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.SendAsync(PostWithMatch($"{Base}/{_templateId}/versions/{_draftId}/actions/publish", "AQ=="), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client($"tpl-{Guid.NewGuid():N}").GetAsync(Base, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync(Base, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- reads ----

    [Fact]
    public async Task List_AndDetail_ShowThePublishedVersionAndTheDraft()
    {
        var admin = Manager();
        var ct = TestContext.Current.CancellationToken;

        using var list = JsonDocument.Parse(await admin.GetStringAsync($"{Base}?language={_language}&channel=Email", ct));
        var row = list.RootElement.EnumerateArray().Should().ContainSingle().Which;
        row.GetProperty("templateId").GetGuid().Should().Be(_templateId);
        row.GetProperty("hasDraft").GetBoolean().Should().BeTrue();
        row.GetProperty("publishedVersion").GetProperty("versionNumber").GetInt32().Should().Be(1);

        using var detail = JsonDocument.Parse(await admin.GetStringAsync($"{Base}/{_templateId}", ct));
        detail.RootElement.GetProperty("isShippedDefault").GetBoolean().Should().BeTrue();
        detail.RootElement.GetProperty("versions").GetArrayLength().Should().Be(2);
        detail.RootElement.GetProperty("declaredVariables").EnumerateArray().Select(v => v.GetProperty("name").GetString())
            .Should().Contain(["workflowName", "failureSummary", "recipientDisplayName"]);

        (await admin.GetAsync($"{Base}/{Guid.NewGuid()}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- preview ----

    [Fact]
    public async Task Preview_UsesTheProductionRenderer_AndTheFixedSampleLink()
    {
        var admin = Manager();

        var response = await admin.PostAsJsonAsync(
            $"{Base}/{_templateId}/versions/{_draftId}/actions/preview",
            new { variables = new Dictionary<string, string> { ["workflowName"] = "Demo <b>flow</b>" } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("subject").GetString().Should().Be("Demo <b>flow</b> failed");
        var html = body.RootElement.GetProperty("html").GetString()!;
        html.Should().Contain("Draft Demo &lt;b&gt;flow&lt;/b&gt;").And.Contain("https://example.invalid/sample-link");
        html.Should().NotContain("<b>flow</b>");
        body.RootElement.GetProperty("text").GetString().Should().Contain("https://example.invalid/sample-link");
        body.RootElement.GetProperty("direction").GetString().Should().Be("ltr");
    }

    // ---- send-test ----

    [Fact]
    public async Task SendTest_GoesOnlyToTheCallersVerifiedAddress_AsTemplateTest_AndRendersTheDraft()
    {
        var (admin, adminId) = await RealAdminAsync();
        var ct = TestContext.Current.CancellationToken;

        var response = await admin.PostAsync($"{Base}/{_templateId}/versions/{_draftId}/actions/send-test", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("t•••@tests.asklucy.io").And.NotContain("tpl-admin-");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var queued = await db.NotificationOutboxEvents.AsNoTracking()
            .SingleAsync(e => e.Type == NotificationTypeKeys.TemplateTest && e.RecipientJson.Contains(adminId), ct);
        queued.ExplicitLanguage.Should().Be(_language);

        // What the delivery pipeline will render for that event is the draft under test, not the published version.
        var variables = JsonSerializer.Deserialize<Dictionary<string, string?>>(queued.VariablesJson)!;
        var rendered = await scope.ServiceProvider.GetRequiredService<INotificationTemplateRenderer>()
            .RenderEmailAsync(NotificationTypeCatalog.Get(NotificationTypeKeys.TemplateTest), "en", variables, ct);
        rendered.HtmlBody.Should().Contain("Draft your workflow").And.NotContain("Published your workflow");
        rendered.Language.Should().Be(_language);

        var audit = await db.NotificationAuditLogs.AsNoTracking().SingleAsync(a => a.ActorUserId == adminId && a.Action == NotificationAuditAction.TemplateTestSent, ct);
        audit.TargetId.Should().Be(_draftId.ToString());
        audit.DetailsJson.Should().NotContain("@tests.asklucy.io");
    }

    [Fact]
    public async Task SendTest_WithoutAVerifiedAddress_Is422_AndQueuesNothing()
    {
        var (admin, adminId) = await RealAdminAsync(verified: false);

        var response = await admin.PostAsync($"{Base}/{_templateId}/versions/{_draftId}/actions/send-test", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.NotificationOutboxEvents.AnyAsync(e => e.Type == NotificationTypeKeys.TemplateTest && e.RecipientJson.Contains(adminId), TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SendTest_TheEleventhInAnHour_Is429()
    {
        var (admin, _) = await RealAdminAsync();
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 10; i++)
        {
            (await admin.PostAsync($"{Base}/{_templateId}/versions/{_draftId}/actions/send-test", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        (await admin.PostAsync($"{Base}/{_templateId}/versions/{_draftId}/actions/send-test", null, ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ---- create / edit / validation ----

    [Fact]
    public async Task CreateDraft_Returns201WithLocation_AndTheNextVersionNumber()
    {
        var admin = Manager();

        var response = await admin.PostAsJsonAsync($"{Base}/{_templateId}/versions", Body(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Contain($"/templates/{_templateId}/versions/");
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("versionNumber").GetInt32().Should().Be(3);
        body.RootElement.GetProperty("status").GetString().Should().Be("Draft");
    }

    [Theory]
    [InlineData("Hello {{ nobody }}", "nobody")]
    [InlineData("Hello {{ workflowName", "{{")]
    [InlineData("See https://evil.example", "Links and HTML")]
    public async Task CreateDraft_WithBadText_Is422NamingTheProblem(string heading, string expected)
    {
        var response = await Manager().PostAsJsonAsync($"{Base}/{_templateId}/versions", Body(heading), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(expected);
    }

    [Fact]
    public async Task UpdateDraft_NeedsIfMatch_AndAStaleOneIsAConcurrencyConflict()
    {
        var admin = Manager();
        var ct = TestContext.Current.CancellationToken;
        var url = $"{Base}/{_templateId}/versions/{_draftId}";

        (await admin.PutAsJsonAsync(url, Body(), ct)).StatusCode.Should().Be((HttpStatusCode)428);

        var token = await RowVersionAsync(admin, _draftId);
        var saved = await admin.SendAsync(Put(url, Body("First edit {{ workflowName }}"), token), ct);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        // The same token again is now stale.
        var stale = await admin.SendAsync(Put(url, Body("Second edit {{ workflowName }}"), token), ct);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = await JsonOf(stale);
        problem.RootElement.GetProperty("reason").GetString().Should().Be("ConcurrencyConflict");
    }

    [Fact]
    public async Task UpdatingThePublishedVersion_Is409VersionNotDraft()
    {
        var admin = Manager();
        var token = await RowVersionAsync(admin, _publishedId);

        var response = await admin.SendAsync(Put($"{Base}/{_templateId}/versions/{_publishedId}", Body(), token), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = await JsonOf(response);
        problem.RootElement.GetProperty("reason").GetString().Should().Be("VersionNotDraft");
    }

    // ---- publish / archive ----

    [Fact]
    public async Task Publish_ArchivesThePreviousVersion_AndKeepsIt_AndEveryActionIsAudited()
    {
        var admin = Manager();
        var ct = TestContext.Current.CancellationToken;
        var token = await RowVersionAsync(admin, _draftId);

        var published = await admin.SendAsync(PostWithMatch($"{Base}/{_templateId}/versions/{_draftId}/actions/publish", token), ct);

        published.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detail = JsonDocument.Parse(await admin.GetStringAsync($"{Base}/{_templateId}", ct));
        detail.RootElement.GetProperty("publishedVersionId").GetGuid().Should().Be(_draftId);
        detail.RootElement.GetProperty("versions").EnumerateArray()
            .ToDictionary(v => v.GetProperty("versionNumber").GetInt32(), v => v.GetProperty("status").GetString())
            .Should().Equal(new Dictionary<int, string?> { [1] = "Archived", [2] = "Published" });

        // The last published version of a shipped default can't be archived.
        var newToken = await RowVersionAsync(admin, _draftId);
        var archive = await admin.SendAsync(PostWithMatch($"{Base}/{_templateId}/versions/{_draftId}/actions/archive", newToken), ct);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = await JsonOf(archive);
        problem.RootElement.GetProperty("reason").GetString().Should().Be("LastPublishedDefault");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var actions = await db.NotificationAuditLogs.AsNoTracking()
            .Where(a => a.TargetId == _draftId.ToString()).Select(a => a.Action).ToListAsync(ct);
        actions.Should().Contain(NotificationAuditAction.TemplateVersionPublished);
    }

    [Fact]
    public async Task Archive_AnUnpublishedDraft_Works_AndIsAudited()
    {
        var admin = Manager();
        var ct = TestContext.Current.CancellationToken;
        var token = await RowVersionAsync(admin, _draftId);

        var response = await admin.SendAsync(PostWithMatch($"{Base}/{_templateId}/versions/{_draftId}/actions/archive", token), ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("Archived");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.NotificationAuditLogs.AsNoTracking().AnyAsync(a => a.TargetId == _draftId.ToString() && a.Action == NotificationAuditAction.TemplateVersionArchived, ct))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ViewingATemplate_IsAuditedOncePerHour()
    {
        var adminId = $"tpl-{Guid.NewGuid():N}";
        var admin = Client(adminId, "admin.notifications.view");
        var ct = TestContext.Current.CancellationToken;

        (await admin.GetAsync($"{Base}/{_templateId}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"{Base}/{_templateId}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.NotificationAuditLogs.AsNoTracking().CountAsync(a => a.ActorUserId == adminId && a.Action == NotificationAuditAction.TemplateViewed, ct)).Should().Be(1);
    }
}
