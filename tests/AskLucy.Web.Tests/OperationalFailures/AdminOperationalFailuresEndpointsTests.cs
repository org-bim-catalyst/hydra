using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using AskLucy.Web.Tests.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

/// <summary>
/// specs/074 T042 — the admin trail's permission gate, and the content decision made on the server
/// from the viewer's stored role: an Administrator sees a chat's metadata, a Super User its
/// transcript. The investigation users are real Identity rows so the handler's own permission
/// resolution runs exactly as in production.
/// </summary>
[Collection(AdministratorContentAccessGroup.Name)]
public sealed class AdminOperationalFailuresEndpointsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private const string BaseUrl = "/api/v1/admin/operational-failures";

    private readonly HttpClient _client = factory.CreateClient();

    private void AuthenticateAs(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task ListIncidents_ShouldReturn403_ForAnOrdinaryUser()
    {
        AuthenticateAs(TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync($"{BaseUrl}/incidents", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListIncidents_ShouldReturn403_ForACustomRoleWithoutView()
    {
        var otherKey = AdminPermissionCatalog.All.First(p => !p.Key.StartsWith("admin.operational-failures", StringComparison.Ordinal)).Key;
        AuthenticateAs(TestJwtFactory.Create("support-1", ["Support"], [otherKey]));

        var response = await _client.GetAsync($"{BaseUrl}/incidents", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListIncidents_ShouldReturn200_ForACustomRoleWithView()
    {
        AuthenticateAs(TestJwtFactory.Create("support-2", ["Support"], [AdminPermissionCatalog.OperationalFailuresView]));

        var response = await _client.GetAsync($"{BaseUrl}/incidents?state=Unresolved&pageSize=5", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task ChatInvestigation_ShouldHaveNoWriteRoute()
    {
        AuthenticateAs(TestJwtFactory.Create("super-1", "Super User"));

        var response = await _client.PostAsync(
            $"{BaseUrl}/incidents/{Guid.NewGuid()}/chats/{Guid.NewGuid()}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ChatInvestigation_ShouldBeNotFound_ForAChatTheIncidentDoesNotReference()
    {
        var seed = await SeedAsync();
        try
        {
            AuthenticateAs(TestJwtFactory.Create(seed.SuperUserId, PrivilegedRoleNames.SuperUser));

            var response = await _client.GetAsync($"{BaseUrl}/incidents/{seed.IncidentId}/chats/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task ChatInvestigation_ShouldWithholdTheTranscriptFromAnAdministrator_AndAuditASuperUsersRead()
    {
        var seed = await SeedAsync();
        try
        {
            AuthenticateAs(TestJwtFactory.Create(seed.AdministratorId, PrivilegedRoleNames.Administrator));
            var asAdministrator = await _client.GetAsync($"{BaseUrl}/incidents/{seed.IncidentId}/chats/{seed.ChatId}", TestContext.Current.CancellationToken);

            asAdministrator.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var body = await ReadJsonAsync(asAdministrator))
            {
                body.RootElement.GetProperty("transcript").ValueKind.Should().Be(JsonValueKind.Null);
                body.RootElement.GetProperty("chat").GetProperty("messageCount").GetInt32().Should().Be(2);
                body.RootElement.GetProperty("failurePoints").GetArrayLength().Should().Be(1);
            }

            (await CountAccessEventsAsync(seed.IncidentId)).Should().Be(0, "metadata alone is not a content access");

            AuthenticateAs(TestJwtFactory.Create(seed.SuperUserId, PrivilegedRoleNames.SuperUser));
            var asSuperUser = await _client.GetAsync($"{BaseUrl}/incidents/{seed.IncidentId}/chats/{seed.ChatId}", TestContext.Current.CancellationToken);

            asSuperUser.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var body = await ReadJsonAsync(asSuperUser))
            {
                var transcript = body.RootElement.GetProperty("transcript");
                transcript.GetArrayLength().Should().Be(2);
                transcript[1].GetProperty("role").GetString().Should().Be("assistant");
                transcript[1].GetProperty("isFailedTurn").GetBoolean().Should().BeTrue();
            }

            (await CountAccessEventsAsync(seed.IncidentId)).Should().Be(1);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task GetIncident_ShouldReportWhetherTheViewerMayReadContent()
    {
        var seed = await SeedAsync();
        try
        {
            AuthenticateAs(TestJwtFactory.Create(seed.AdministratorId, PrivilegedRoleNames.Administrator));
            using (var body = await ReadJsonAsync(await _client.GetAsync($"{BaseUrl}/incidents/{seed.IncidentId}", TestContext.Current.CancellationToken)))
            {
                body.RootElement.GetProperty("canViewContent").GetBoolean().Should().BeFalse();
                body.RootElement.GetProperty("canManage").GetBoolean().Should().BeTrue();
                body.RootElement.GetProperty("engine").GetString().Should().Be("Chat");
            }

            AuthenticateAs(TestJwtFactory.Create(seed.SuperUserId, PrivilegedRoleNames.SuperUser));
            using (var body = await ReadJsonAsync(await _client.GetAsync($"{BaseUrl}/incidents/{seed.IncidentId}", TestContext.Current.CancellationToken)))
            {
                body.RootElement.GetProperty("canViewContent").GetBoolean().Should().BeTrue();
            }
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private sealed record Seed(Guid IncidentId, Guid ChatId, string OwnerId, string AdministratorId, string SuperUserId);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);

    private async Task<Seed> SeedAsync()
    {
        var ownerId = await SeedUserAsync(role: null);
        var administratorId = await SeedUserAsync(PrivilegedRoleNames.Administrator);
        var superUserId = await SeedUserAsync(PrivilegedRoleNames.SuperUser);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        var chat = UserChat.Create("Budget review", ownerId, null, ownerId);
        chat.Id = Guid.NewGuid();
        var question = Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "What is the budget?", null, ownerId);
        question.Id = Guid.NewGuid();
        var reply = Message.Create(chat.Id, MessageRole.Assistant, MessageKind.Text, "The budget is", null, ownerId);
        reply.Id = Guid.NewGuid();
        // Saved one at a time, as a real turn is: the audit interceptor stamps both with one clock
        // reading otherwise, and the transcript's order would tie.
        db.UserChats.Add(chat);
        db.Messages.Add(question);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Messages.Add(reply);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Its own grouping key, so this never joins an incident the real pipeline wrote.
        var now = DateTime.UtcNow;
        var incident = OperationalFailureIncident.Open(
            $"endpoint-test:{Guid.NewGuid():N}", $"endpoint-test:{Guid.NewGuid():N}", OperationalFailureEngine.Chat, "Chat reply",
            OperationalFailureKind.CredentialRejected, OperationalFailureSeverity.Critical, now, "The provider rejected the credential",
            "corr-endpoint-test", providerName: "OpenAI");
        incident.Id = Guid.NewGuid();
        typeof(OperationalFailureIncident).GetProperty(nameof(OperationalFailureIncident.OccurrenceCount))!
            .GetSetMethod(nonPublic: true)!.Invoke(incident, [1]);
        db.OperationalFailureIncidents.Add(incident);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var occurrence = OperationalFailureOccurrence.Create(
            incident.Id, now, OperationalFailureSeverity.Critical, OperationalFailureKind.CredentialRejected, OperationalFailureEngine.Chat,
            "Chat reply", "The provider rejected the credential", "corr-endpoint-test",
            new OperationalFailureReferences { UserId = ownerId, ChatId = chat.Id, MessageId = reply.Id }, "OpenAI");
        occurrence.Id = Guid.NewGuid();
        db.OperationalFailureOccurrences.Add(occurrence);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new Seed(incident.Id, chat.Id, ownerId, administratorId, superUserId);
    }

    private async Task<string> SeedUserAsync(string? role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"of-endpoint-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };

        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        if (role is not null)
        {
            (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        }

        return user.Id;
    }

    private async Task<int> CountAccessEventsAsync(Guid incidentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        return await db.UserContentAccessEvents.CountAsync(e => e.IncidentId == incidentId, TestContext.Current.CancellationToken);
    }

    private async Task CleanupAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var ct = TestContext.Current.CancellationToken;

        await db.UserContentAccessEvents.Where(e => e.IncidentId == seed.IncidentId).ExecuteDeleteAsync(ct);
        await db.OperationalFailureOccurrences.Where(o => o.IncidentId == seed.IncidentId).ExecuteDeleteAsync(ct);
        await db.OperationalFailureIncidentParticipants.Where(p => p.IncidentId == seed.IncidentId).ExecuteDeleteAsync(ct);
        await db.OperationalFailureIncidents.Where(i => i.Id == seed.IncidentId).ExecuteDeleteAsync(ct);
        await db.Messages.IgnoreQueryFilters().Where(m => m.UserChatId == seed.ChatId).ExecuteDeleteAsync(ct);
        await db.UserChats.IgnoreQueryFilters().Where(c => c.Id == seed.ChatId).ExecuteDeleteAsync(ct);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var userId in new[] { seed.OwnerId, seed.AdministratorId, seed.SuperUserId })
        {
            if (await userManager.FindByIdAsync(userId) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }
}
