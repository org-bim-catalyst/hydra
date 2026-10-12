using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.TwoFactor;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Domain.Documents;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// specs/067 T080 (US2 Independent Test): every emitted, in-center type in <see cref="NotificationTypeCatalog"/>
/// is requested through the path its module uses, committed with <see cref="IUnitOfWork"/>, left to the real
/// background dispatcher, and must produce exactly one center item with the right category, priority, route
/// and recipient. Expectations are written out here, not read from the catalogue, so a catalogue edit that
/// changes a route or priority fails this test.
/// <para>
/// How each type is covered (see also the T080 note in tasks.md):
/// </para>
/// <list type="bullet">
/// <item><description><b>document.*</b> (upload, processing completed/failed, ocr completed/failed, version, storage limit) and <b>memory.*</b> (3 types): the real <see cref="IProcessingNotifier"/> and <see cref="IMemoryNotifier"/> from DI, the same objects the document pipeline and memory engine call.</description></item>
/// <item><description><b>security.two-factor.enabled / disabled / recovery-codes.regenerated</b>: the real <c>Enable/Disable TwoFactor</c> and <c>GenerateRecoveryCodes</c> commands through MediatR.</description></item>
/// <item><description><b>system.announcement.published</b>: the real admin <c>POST /announcements</c> endpoint, aimed at a role holding only the test recipient.</description></item>
/// <item><description><b>agent.* (4), workflow.* (5), document.indexing.*, knowledge-base.indexing.*</b>: the real <see cref="INotificationPublisher"/> with the exact request each emitting site builds (type, recipient, variables, related item, event key), committed through <see cref="IUnitOfWork"/>. Driving the orchestrators and the indexing job needs full agent, workflow and vector-store graphs; their emit calls are unit-tested (T075 to T078) and the indexing pair is also covered end to end by <c>KnowledgeBaseIndexingEndToEndTests</c> (T236).</description></item>
/// <item><description><b>Not covered here</b>: <c>knowledge-base.updated</c> (defined, never emitted, research R27); the not-emitted conversation and billing types; the email-only <c>account.*</c>, <c>security.password-changed</c> and <c>template.test</c> types, which have no center item by design and are covered by <c>AccountEmail*Tests</c> and <c>NotificationTemplateEndpointsTests</c>.</description></item>
/// </list>
/// </summary>
public sealed class EmitterEndToEndTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly List<string> _userIds = [];
    private readonly List<string> _roleIds = [];
    private readonly List<string> _adminIds = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var ids = _userIds.Concat(_adminIds).ToList();

        foreach (var userId in _userIds)
        {
            await db.NotificationOutboxEvents.Where(e => e.RecipientJson.Contains(userId)).ExecuteDeleteAsync();
        }

        await db.NotificationOutboxEvents.Where(e => e.EventKey != null && e.EventKey.StartsWith("announcement:")
            && db.SystemAnnouncements.Any(a => _adminIds.Contains(a.PublishedByUserId) && e.RelatedItemId == a.Id.ToString())).ExecuteDeleteAsync();
        await db.SystemAnnouncements.IgnoreQueryFilters().Where(a => _adminIds.Contains(a.PublishedByUserId)).ExecuteDeleteAsync();
        await db.NotificationAuditLogs.IgnoreQueryFilters().Where(a => a.ActorUserId != null && _adminIds.Contains(a.ActorUserId)).ExecuteDeleteAsync();
        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId != null && ids.Contains(n.RecipientUserId)).ExecuteDeleteAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in ids)
        {
            if (await users.FindByIdAsync(id) is { } user)
            {
                await users.DeleteAsync(user);
            }
        }

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var id in _roleIds)
        {
            if (await roles.FindByIdAsync(id) is { } role)
            {
                await roles.DeleteAsync(role);
            }
        }
    }

    /// <summary>One expected center item: the type, and what its notification must carry.</summary>
    private sealed record Expectation(
        string Type, NotificationCategory Category, NotificationPriority Priority, string Route, Func<IServiceProvider, string, Task> Emit);

    [Fact]
    public async Task EveryEmittedType_ProducesExactlyOneCenterItem_WithItsCategoryPriorityRouteAndRecipient()
    {
        var ct = TestContext.Current.CancellationToken;
        var recipient = await SeedUserAsync("emitter");
        var bystander = await SeedUserAsync("emitter-bystander");

        var execution = Guid.NewGuid();
        var agent = Guid.NewGuid();
        var workflow = Guid.NewGuid();
        var approval = Guid.NewGuid();
        var document = Guid.NewGuid();
        var knowledgeBase = Guid.NewGuid();
        var memory = Guid.NewGuid();

        // The item-ownership checks are the only thing not production here: seeding a real agent run, workflow run, document, knowledge base and
        // memory per case would test those modules' persistence. They answer "yes" for exactly these ids, in every host (see the allow-list);
        // the checks themselves are covered by ApprovalNotificationAccessTests and the per-module unit tests.
        NotificationAccessAllowList.Allow(execution, agent, workflow, approval, document, knowledgeBase, memory);

        var agentRoute = $"/agents/{agent}/executions/{execution}";
        var workflowRoute = $"/workflows/{workflow}/executions/{execution}";
        var documentRoute = $"/documents?documentId={document}";
        var knowledgeBaseRoute = $"/knowledge-bases/{knowledgeBase}";
        var memoryRoute = $"/memory?memoryId={memory}";

        var cases = new List<Expectation>
        {
            // agents: AgentExecutionOrchestrator
            Agent(NotificationTypeKeys.AgentExecutionStarted, NotificationPriority.Low, agentRoute, "started", execution, agent, ("agentName", "Research Agent")),
            Agent(NotificationTypeKeys.AgentExecutionCompleted, NotificationPriority.Normal, agentRoute, "completed", execution, agent, ("agentName", "Research Agent"), ("duration", "12s")),
            Agent(NotificationTypeKeys.AgentExecutionFailed, NotificationPriority.High, agentRoute, "failed", execution, agent, ("agentName", "Research Agent"), ("failureSummary", "The model timed out.")),
            Publish(
                NotificationTypeKeys.AgentApprovalRequested, NotificationCategory.Agent, NotificationPriority.High, $"{agentRoute}?approval={approval}",
                new RelatedItem("AgentExecution", execution.ToString(), agent.ToString()), $"agent-approval:{approval}:requested",
                ("agentName", "Research Agent"), ("intendedAction", "Send an email"), ("approvalId", approval.ToString())),

            // workflows: WorkflowExecutionOrchestrator and PauseWorkflowExecutionCommandHandler
            Workflow(NotificationTypeKeys.WorkflowExecutionStarted, NotificationPriority.Low, workflowRoute, "started", execution, workflow, ("workflowName", "Weekly report")),
            Workflow(NotificationTypeKeys.WorkflowExecutionCompleted, NotificationPriority.Normal, workflowRoute, "completed", execution, workflow, ("workflowName", "Weekly report"), ("duration", "1m 4s")),
            Workflow(NotificationTypeKeys.WorkflowExecutionFailed, NotificationPriority.High, workflowRoute, "failed", execution, workflow, ("workflowName", "Weekly report"), ("failureSummary", "A step failed.")),
            Workflow(NotificationTypeKeys.WorkflowExecutionPaused, NotificationPriority.Normal, workflowRoute, "paused", execution, workflow, ("workflowName", "Weekly report")),
            Publish(
                NotificationTypeKeys.WorkflowApprovalRequested, NotificationCategory.Workflow, NotificationPriority.High, $"{workflowRoute}?approval={approval}",
                new RelatedItem("WorkflowExecution", execution.ToString(), workflow.ToString()), $"workflow-approval:{approval}:requested",
                ("workflowName", "Weekly report"), ("nodeName", "send-mail"), ("intendedAction", "Send an email"), ("approvalId", approval.ToString())),

            // documents: ProcessingNotifier
            Notifier(NotificationTypeKeys.DocumentUploadCompleted, NotificationPriority.Normal, documentRoute,
                (n, user) => n.NotifyAsync(user, DocumentNotificationEventType.UploadCompleted, document, "e2e", documentName: "Spec.pdf")),
            Notifier(NotificationTypeKeys.DocumentProcessingCompleted, NotificationPriority.Normal, documentRoute,
                (n, user) => n.NotifyAsync(user, DocumentNotificationEventType.ProcessingCompleted, document, "e2e", documentName: "Spec.pdf")),
            Notifier(NotificationTypeKeys.DocumentProcessingFailed, NotificationPriority.High, documentRoute,
                (n, user) => n.NotifyAsync(user, DocumentNotificationEventType.ProcessingFailed, document, "e2e", documentName: "Spec.pdf", failureSummary: "Unreadable file.")),
            Notifier(NotificationTypeKeys.DocumentOcrCompleted, NotificationPriority.Normal, documentRoute,
                (n, user) => n.NotifyOcrCompletedAsync(user, document, "Scan.pdf", "e2e")),
            Notifier(NotificationTypeKeys.DocumentOcrFailed, NotificationPriority.High, documentRoute,
                (n, user) => n.NotifyAsync(user, DocumentNotificationEventType.OcrFailed, document, "e2e", documentName: "Scan.pdf", failureSummary: "No text found.")),
            Notifier(NotificationTypeKeys.DocumentVersionCreated, NotificationPriority.Normal, documentRoute,
                (n, user) => n.NotifyAsync(user, DocumentNotificationEventType.VersionCreated, document, "2", documentName: "Spec.pdf", versionNumber: "2")),
            Notifier(NotificationTypeKeys.DocumentStorageLimitReached, NotificationPriority.High, "/documents",
                (n, user) => n.NotifyAsync(user, DocumentNotificationEventType.StorageLimitReached, null, "2026-10-07", usedStorage: "1.9 GB", storageLimit: "2 GB")),

            // retrieval indexing: KnowledgeBaseIndexingJob
            Publish(
                NotificationTypeKeys.DocumentIndexingCompleted, NotificationCategory.Document, NotificationPriority.Normal, documentRoute,
                new RelatedItem("Document", document.ToString()), $"indexing-job:{Guid.NewGuid()}:completed", ("documentName", "Spec.pdf")),
            Publish(
                NotificationTypeKeys.DocumentIndexingFailed, NotificationCategory.Document, NotificationPriority.High, documentRoute,
                new RelatedItem("Document", document.ToString()), $"indexing-job:{Guid.NewGuid()}:failed", ("documentName", "Spec.pdf"), ("failureSummary", "Embedding failed.")),
            Publish(
                NotificationTypeKeys.KnowledgeBaseIndexingCompleted, NotificationCategory.KnowledgeBase, NotificationPriority.Normal, knowledgeBaseRoute,
                new RelatedItem("KnowledgeBase", knowledgeBase.ToString()), $"knowledge-base:{knowledgeBase}:indexing:1", ("knowledgeBaseName", "Standards")),
            Publish(
                NotificationTypeKeys.KnowledgeBaseIndexingFailed, NotificationCategory.KnowledgeBase, NotificationPriority.High, knowledgeBaseRoute,
                new RelatedItem("KnowledgeBase", knowledgeBase.ToString()), $"knowledge-base:{knowledgeBase}:indexing:2", ("knowledgeBaseName", "Standards"), ("failureSummary", "Embedding failed.")),

            // memory: MemoryNotifier
            Memory(NotificationTypeKeys.MemoryAutoCreated, NotificationPriority.Low, memoryRoute, memory, MemoryNotificationEventType.AutoCreated),
            Memory(NotificationTypeKeys.MemoryAutoApproved, NotificationPriority.Low, memoryRoute, memory, MemoryNotificationEventType.AutoApproved),
            Memory(NotificationTypeKeys.MemoryConflictConfirmationNeeded, NotificationPriority.Normal, memoryRoute, memory, MemoryNotificationEventType.ConflictNeedsConfirmation),
        };

        foreach (var expectation in cases)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await expectation.Emit(scope.ServiceProvider, recipient);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }

        // Security: the three real commands, in the order their preconditions allow.
        var security = new (string Type, Func<IMediator, Task> Run)[]
        {
            (NotificationTypeKeys.SecurityTwoFactorEnabled, m => m.Send(new EnableTwoFactorCommand(recipient), ct)),
            (NotificationTypeKeys.SecurityRecoveryCodesRegenerated, m => m.Send(new GenerateRecoveryCodesCommand(recipient), ct)),
            (NotificationTypeKeys.SecurityTwoFactorDisabled, m => m.Send(new DisableTwoFactorCommand(recipient), ct)),
        };
        foreach (var (_, run) in security)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await run(scope.ServiceProvider.GetRequiredService<IMediator>());
        }

        var expectedTypes = cases.Select(c => c.Type).Concat(security.Select(s => s.Type)).ToList();
        var delivered = await WaitForCenterItemsAsync(recipient, expectedTypes.Count);
        await Task.Delay(TimeSpan.FromSeconds(2), ct); // a duplicate, if any, would land by now
        delivered = await CenterItemsAsync(recipient);

        foreach (var type in expectedTypes)
        {
            delivered.Count(n => n.Type == type).Should().Be(1, $"'{type}' must produce exactly one center item (got: {string.Join(", ", delivered.Select(d => d.Type))})");
        }

        delivered.Should().HaveCount(expectedTypes.Count, "nothing else may reach the recipient's center");

        foreach (var expectation in cases)
        {
            var item = delivered.Single(n => n.Type == expectation.Type);
            item.RecipientUserId.Should().Be(recipient, expectation.Type);
            item.Category.Should().Be(expectation.Category, expectation.Type);
            item.Priority.Should().Be(expectation.Priority, expectation.Type);
            item.ActionRoute.Should().Be(expectation.Route, expectation.Type);
            item.ShowInCenter.Should().BeTrue(expectation.Type);
            item.Status.Should().NotBe(NotificationStatus.Cancelled, expectation.Type);
        }

        foreach (var (type, _) in security)
        {
            var item = delivered.Single(n => n.Type == type);
            item.RecipientUserId.Should().Be(recipient, type);
            item.Category.Should().Be(NotificationCategory.Security, type);
            item.Priority.Should().Be(NotificationPriority.Critical, type);
            item.ActionRoute.Should().Be("/settings?tab=security", type);
        }

        (await CenterItemsAsync(bystander)).Should().BeEmpty("nobody but the recipient is notified");
    }

    [Fact]
    public async Task ASystemAnnouncement_PublishedThroughTheAdminEndpoint_ProducesExactlyOneCenterItem_ForEachMemberOfItsAudience()
    {
        var ct = TestContext.Current.CancellationToken;
        var member = await SeedUserAsync("announce-member");
        var outsider = await SeedUserAsync("announce-outsider");

        string roleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var role = new ApplicationRole($"announce-e2e-{Guid.NewGuid():N}");
            (await scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>().CreateAsync(role)).Succeeded.Should().BeTrue();
            roleId = role.Id;
            _roleIds.Add(roleId);

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.AddToRoleAsync((await users.FindByIdAsync(member))!, role.Name!)).Succeeded.Should().BeTrue();
        }

        var adminId = await SeedUserAsync("announce-admin", administrator: true, admin: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(adminId, ["Administrator"], []));

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/notifications/announcements",
            new { kind = "Maintenance", title = "Scheduled maintenance", message = "Ask Lucy will be unavailable for an hour.", audience = "Roles", targetRoleIds = new[] { roleId }, isCritical = false, endsAtUtc = DateTime.UtcNow.AddDays(1) },
            ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));

        var items = await WaitForCenterItemsAsync(member, 1);
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        items = await CenterItemsAsync(member);

        var item = items.Should().ContainSingle().Subject;
        item.Type.Should().Be(NotificationTypeKeys.SystemAnnouncementPublished);
        item.RecipientUserId.Should().Be(member);
        item.Category.Should().Be(NotificationCategory.System);
        item.Priority.Should().Be(NotificationPriority.Normal);
        item.ActionRoute.Should().Be($"/notifications/{item.Id}", "an announcement's route is its own detail page");
        (await CenterItemsAsync(outsider)).Should().BeEmpty("only the targeted role is notified");
    }

    // ---- expectations ----

    private static Expectation Agent(
        string type, NotificationPriority priority, string route, string @event, Guid execution, Guid agent, params (string Name, string Value)[] variables) =>
        Publish(type, NotificationCategory.Agent, priority, route, new RelatedItem("AgentExecution", execution.ToString(), agent.ToString()), $"agent-execution:{execution}:{@event}", variables);

    private static Expectation Workflow(
        string type, NotificationPriority priority, string route, string @event, Guid execution, Guid workflow, params (string Name, string Value)[] variables) =>
        Publish(type, NotificationCategory.Workflow, priority, route, new RelatedItem("WorkflowExecution", execution.ToString(), workflow.ToString()), $"workflow-execution:{execution}:{@event}", variables);

    /// <summary>The request an emitting site builds, sent through the real publisher.</summary>
    private static Expectation Publish(
        string type, NotificationCategory category, NotificationPriority priority, string route, RelatedItem related, string eventKey, params (string Name, string Value)[] variables) =>
        new(type, category, priority, route, (services, userId) =>
        {
            services.GetRequiredService<INotificationPublisher>().Publish(new NotificationRequest(
                type,
                new NotificationRecipient.User(userId),
                variables.ToDictionary(v => v.Name, v => (string?)v.Value),
                related,
                EventKey: eventKey));
            return Task.CompletedTask;
        });

    private static Expectation Notifier(string type, NotificationPriority priority, string route, Func<IProcessingNotifier, string, Task> emit) =>
        new(type, NotificationCategory.Document, priority, route, (services, userId) => emit(services.GetRequiredService<IProcessingNotifier>(), userId));

    private static Expectation Memory(string type, NotificationPriority priority, string route, Guid memoryId, MemoryNotificationEventType eventType) =>
        new(type, NotificationCategory.Memory, priority, route,
            (services, userId) => services.GetRequiredService<IMemoryNotifier>().NotifyAsync(userId, memoryId, eventType, "Prefers metric units."));

    // ---- plumbing ----

    private async Task<string> SeedUserAsync(string prefix, bool administrator = false, bool admin = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{prefix}-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        if (administrator)
        {
            (await users.AddToRoleAsync(user, "Administrator")).Succeeded.Should().BeTrue();
        }

        (admin ? _adminIds : _userIds).Add(user.Id);
        return user.Id;
    }

    private async Task<List<Notification>> CenterItemsAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        return await db.Notifications.AsNoTracking().IgnoreQueryFilters()
            .Where(n => n.RecipientUserId == userId && n.ShowInCenter)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Waits for the host's own dispatcher. The shared database backs every host in the run, so another host may hold
    /// the claim on one of this test's outbox rows for a while; this host keeps draining too while it waits.
    /// </summary>
    private async Task<List<Notification>> WaitForCenterItemsAsync(string userId, int expected)
    {
        var deadline = DateTime.UtcNow.AddMinutes(6);
        var nextFlush = DateTime.UtcNow.AddSeconds(3);
        List<Notification> items;
        do
        {
            items = await CenterItemsAsync(userId);
            if (items.Count >= expected)
            {
                return items;
            }

            if (DateTime.UtcNow >= nextFlush)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxDispatchService>().DispatchBatchAsync("emitter-e2e-test-worker", TestContext.Current.CancellationToken);
                nextFlush = DateTime.UtcNow.AddSeconds(3);
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        return items;
    }
}
