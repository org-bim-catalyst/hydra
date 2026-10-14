using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.Workflows;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MessagePublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>One host and database for the approval-notification tests.</summary>
public sealed class ApprovalNotificationFactory : CustomWebApplicationFactory;

/// <summary>
/// T144 — specs/067 US5 (FR-054, SC-008), over the real host and database, with real agent and workflow executions.
/// The approver is the execution's owner: a signed-out caller gets 401, anyone else gets 404 (never a hint that the item
/// exists), and the notification's creation, email hand-off and first read each leave exactly one audit row carrying the
/// event's correlation id.
/// </summary>
public sealed class ApprovalNotificationAccessTests(ApprovalNotificationFactory factory)
    : IClassFixture<ApprovalNotificationFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<string> _userIds = [];
    private readonly List<Guid> _agentIds = [];
    private readonly List<Guid> _workflowIds = [];
    private string _owner = string.Empty;
    private string _other = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _owner = await SeedUserAsync();
        _other = await SeedUserAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var ids = _userIds.ToList();
        var notificationIds = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.RecipientUserId != null && ids.Contains(n.RecipientUserId)).Select(n => n.Id.ToString()).ToListAsync();
        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId != null && ids.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        await db.NotificationOutboxEvents.Where(e => e.EventKey != null && e.EventKey.StartsWith("approval-access-test:")).ExecuteDeleteAsync();
        await db.NotificationAuditLogs.IgnoreQueryFilters().Where(a => notificationIds.Contains(a.TargetId)).ExecuteDeleteAsync();
        await db.AgentExecutions.IgnoreQueryFilters().Where(e => ids.Contains(e.RunByUserId)).ExecuteDeleteAsync();
        await db.WorkflowExecutions.IgnoreQueryFilters().Where(e => ids.Contains(e.RunByUserId)).ExecuteDeleteAsync();

        // Versions are restricted (kept for audit while their agent or workflow exists), so they go first; their nodes and tools cascade.
        await db.AgentVersions.IgnoreQueryFilters().Where(v => _agentIds.Contains(v.AgentId)).ExecuteDeleteAsync();
        await db.Agents.IgnoreQueryFilters().Where(a => _agentIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.WorkflowVersions.IgnoreQueryFilters().Where(v => _workflowIds.Contains(v.WorkflowId)).ExecuteDeleteAsync();
        await db.Workflows.IgnoreQueryFilters().Where(w => _workflowIds.Contains(w.Id)).ExecuteDeleteAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in ids)
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
        var email = $"approval-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        _userIds.Add(user.Id);
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

    private async Task<(Guid ExecutionId, Guid AgentId, Guid ApprovalId)> SeedAgentApprovalAsync()
    {
        var agent = Agent.Create(
            _owner, $"Approval agent {Guid.NewGuid():N}", null, AgentType.Task,
            new AgentInstructions("You are a helpful assistant.", null, null, null, null, null, null),
            Guid.NewGuid(), Guid.NewGuid(), AgentOutputFormat.PlainText, AgentExecutionPolicy.Empty, _owner);
        var version = agent.Publish(null, _owner);
        var execution = AgentExecution.Create(agent.Id, version.Id, _owner, "Do the risky thing.", false, AgentConversationIntegrationMode.Standalone, null, _owner);
        execution.Start();
        var approval = execution.RequestApproval(null, "Execute FakeHighRiskTool", "{}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        db.Agents.Add(agent);
        db.AgentExecutions.Add(execution);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _agentIds.Add(agent.Id);
        return (execution.Id, agent.Id, approval.Id);
    }

    private async Task<(Guid ExecutionId, Guid WorkflowId, Guid ApprovalId)> SeedWorkflowApprovalAsync()
    {
        static WorkflowNodeSpec Node(string key, WorkflowNodeType type) =>
            new(key, type, key, null, "{}", "{}", "{}", "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);

        var workflow = Workflow.Create(_owner, $"Approval workflow {Guid.NewGuid():N}", null, WorkflowType.Manual, _owner);
        var version = workflow.Publish(
            [Node("start", WorkflowNodeType.Start), Node("approval", WorkflowNodeType.HumanApproval), Node("end", WorkflowNodeType.End)],
            [new WorkflowConnectionSpec("start", "approval", null, null), new WorkflowConnectionSpec("approval", "end", null, null)],
            [], "{}", "{}", "{}", "{}", "{}", null, _owner);
        var execution = WorkflowExecution.Create(workflow.Id, version.Id, _owner, WorkflowExecutionTriggerType.Manual, null, "{}", _owner);
        execution.Start();
        var node = execution.AddNode(version.Nodes.Single(n => n.NodeKey == "approval").Id);
        node.Start(inputJson: null);
        node.WaitForApproval();
        var approval = execution.RequestApproval(node.Id, "Proceed past the 'approval' approval step", "{}", null);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        db.Workflows.Add(workflow);
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _workflowIds.Add(workflow.Id);
        return (execution.Id, workflow.Id, approval.Id);
    }

    /// <summary>Publishes an agent approval request the way the orchestrator does, and waits for the hub to materialize it.</summary>
    private async Task<Notification> RequestAgentApprovalNotificationAsync(Guid executionId, Guid agentId, Guid approvalId)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<MessagePublisher>().Publish(new NotificationRequest(
                NotificationTypeKeys.AgentApprovalRequested,
                new NotificationRecipient.User(_owner),
                new Dictionary<string, string?>
                {
                    ["agentName"] = "Approval agent",
                    ["intendedAction"] = "Execute FakeHighRiskTool",
                    ["approvalId"] = approvalId.ToString(),
                },
                new RelatedItem("AgentExecution", executionId.ToString(), agentId.ToString()),
                EventKey: $"approval-access-test:{approvalId}"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return await WaitForNotificationAsync(_owner, n => n.Deliveries.Count >= 2);
    }

    private async Task<Notification> WaitForNotificationAsync(string userId, Func<Notification, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
            var notification = await db.Notifications.IgnoreQueryFilters().Include(n => n.Deliveries)
                .FirstOrDefaultAsync(n => n.RecipientUserId == userId, TestContext.Current.CancellationToken);
            if (notification is not null && ready(notification))
            {
                return notification;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"The expected notification for {userId} didn't appear within 30 s.");
    }

    private async Task<List<NotificationAuditLog>> AuditRowsAsync(Notification notification)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        return await db.NotificationAuditLogs.IgnoreQueryFilters()
            .Where(a => a.TargetType == nameof(Notification) && a.TargetId == notification.Id.ToString())
            .OrderBy(a => a.OccurredAtUtc).ToListAsync(TestContext.Current.CancellationToken);
    }

    // ---- signed out: 401 ----

    [Fact]
    public async Task SignedOut_EveryApprovalSurface_Returns401()
    {
        var client = ClientFor(null);
        var execution = Guid.NewGuid();
        var approval = Guid.NewGuid();

        (await client.GetAsync($"/api/v1/notifications/{Guid.NewGuid()}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/actions/mark-read", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/agent-executions/{execution}/approvals/{approval}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"/api/v1/agent-executions/{execution}/approvals/{approval}/approve", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/workflow-executions/{execution}/approvals/{approval}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"/api/v1/workflow-executions/{execution}/approvals/{approval}/approve", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- another user: 404 ----

    [Fact]
    public async Task AnotherUser_GetsA404_OnTheNotification_AndOnTheAgentApproval()
    {
        var (executionId, agentId, approvalId) = await SeedAgentApprovalAsync();
        var notification = await RequestAgentApprovalNotificationAsync(executionId, agentId, approvalId);
        var intruder = ClientFor(_other);

        (await intruder.GetAsync($"/api/v1/notifications/{notification.Id}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PostAsync($"/api/v1/notifications/{notification.Id}/actions/mark-read", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetAsync($"/api/v1/agent-executions/{executionId}/approvals/{approvalId}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PostAsync($"/api/v1/agent-executions/{executionId}/approvals/{approvalId}/approve", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PostAsync($"/api/v1/agent-executions/{executionId}/approvals/{approvalId}/reject", JsonContent.Create(new { reason = "no" }), TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The refused attempts changed nothing: still pending, and the owner can still see it.
        var owner = await ClientFor(_owner).GetAsync($"/api/v1/agent-executions/{executionId}/approvals/{approvalId}", TestContext.Current.CancellationToken);
        owner.StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Pending");
    }

    [Fact]
    public async Task AnotherUser_GetsA404_OnTheWorkflowApproval()
    {
        var (executionId, _, approvalId) = await SeedWorkflowApprovalAsync();
        var intruder = ClientFor(_other);

        (await intruder.GetAsync($"/api/v1/workflow-executions/{executionId}/approvals/{approvalId}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PostAsync($"/api/v1/workflow-executions/{executionId}/approvals/{approvalId}/approve", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ClientFor(_owner).GetAsync($"/api/v1/workflow-executions/{executionId}/approvals/{approvalId}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OnlyTheApprover_IsNotified_AndTheLinkOpensThatApproval()
    {
        var (executionId, agentId, approvalId) = await SeedAgentApprovalAsync();
        var notification = await RequestAgentApprovalNotificationAsync(executionId, agentId, approvalId);

        notification.RecipientUserId.Should().Be(_owner);
        notification.ActionRoute.Should().Be($"/agents/{agentId}/executions/{executionId}?approval={approvalId}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.RecipientUserId == _other, TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ---- audit trail ----

    [Fact]
    public async Task CreationDeliveryAndFirstRead_EachLeaveOneAuditRow_WithTheCorrelationId()
    {
        var (executionId, agentId, approvalId) = await SeedAgentApprovalAsync();
        var notification = await RequestAgentApprovalNotificationAsync(executionId, agentId, approvalId);

        // The email goes out on the host's own delivery worker.
        notification = await WaitForNotificationAsync(_owner, n => n.Deliveries.Any(d => d.Channel == NotificationChannel.Email && d.Status == DeliveryStatus.Sent));
        var owner = ClientFor(_owner);
        (await owner.PostAsync($"/api/v1/notifications/{notification.Id}/actions/mark-read", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.PostAsync($"/api/v1/notifications/{notification.Id}/actions/mark-read", null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await AuditRowsAsync(notification);
        rows.Select(r => r.Action).Should().BeEquivalentTo(
            [NotificationAuditAction.ApprovalNotificationCreated, NotificationAuditAction.ApprovalNotificationDelivered, NotificationAuditAction.ApprovalNotificationRead],
            "each step is recorded once, and opening it a second time isn't a second read");
        rows.Should().OnlyContain(r => r.CorrelationId == notification.CorrelationId && !string.IsNullOrEmpty(r.CorrelationId));
        rows.Should().OnlyContain(r => r.Outcome == NotificationAuditOutcome.Succeeded);
        rows.Single(r => r.Action == NotificationAuditAction.ApprovalNotificationRead).ActorUserId.Should().Be(_owner);
        rows.Single(r => r.Action == NotificationAuditAction.ApprovalNotificationCreated).ActorUserId.Should().BeNull("the hub created it, not a person");
        rows.SelectMany(r => new[] { r.DetailsJson ?? string.Empty }).Should().OnlyContain(d => !d.Contains("FakeHighRiskTool"), "an audit row never carries the request's content");
    }

    [Fact]
    public async Task ARefusedAttemptByAnotherUser_AddsNoReadAuditRow()
    {
        var (executionId, agentId, approvalId) = await SeedAgentApprovalAsync();
        var notification = await RequestAgentApprovalNotificationAsync(executionId, agentId, approvalId);

        (await ClientFor(_other).PostAsync($"/api/v1/notifications/{notification.Id}/actions/mark-read", null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await AuditRowsAsync(notification)).Should().NotContain(r => r.Action == NotificationAuditAction.ApprovalNotificationRead);
    }
}
