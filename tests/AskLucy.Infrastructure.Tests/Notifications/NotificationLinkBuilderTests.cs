using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>T011 — <see cref="NotificationLinkBuilder"/> (research R11): route templates are filled
/// from the related item, every substituted value is URL-encoded, and a route that would leave the
/// app is refused rather than linked to.</summary>
public sealed class NotificationLinkBuilderTests
{
    private static readonly NotificationTypeDefinition AgentExecutionCompleted =
        NotificationTypeCatalog.Get(NotificationTypeKeys.AgentExecutionCompleted);

    private readonly FakeLogger<NotificationLinkBuilder> _logger = new();

    private NotificationLinkBuilder CreateSut(string frontendBaseUrl = "https://tests.asklucy.io") =>
        new(Options.Create(new AppOptions { FrontendBaseUrl = frontendBaseUrl }), _logger);

    [Fact]
    public void BuildRelative_ShouldSubstituteEveryToken_UrlEncoded()
    {
        var sut = CreateSut();
        var relatedItem = new RelatedItem("AgentExecution", "exec 1", "agent/1");

        var route = sut.BuildRelative(AgentExecutionCompleted, relatedItem, Guid.NewGuid());

        route.Should().Be("/agents/agent%2F1/executions/exec%201");
    }

    [Fact]
    public void BuildRelative_ShouldReturnNull_AndLogAWarning_WhenTheRelatedItemDoesntFillTheRoute()
    {
        var sut = CreateSut();

        var route = sut.BuildRelative(AgentExecutionCompleted, relatedItem: null, Guid.NewGuid());

        route.Should().BeNull();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning)
            .Which.Message.Should().Contain(AgentExecutionCompleted.Key);
    }

    [Fact]
    public void BuildRelative_ShouldThrow_WhenTheRouteWouldLeaveTheApp_AndTheTypeDoesntAllowExternalLinks()
    {
        var definition = AgentExecutionCompleted with { RouteTemplate = "https://evil.example.com/{id}" };
        var sut = CreateSut();

        var act = () => sut.BuildRelative(definition, new RelatedItem("AgentExecution", "1"), Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildAbsolute_ShouldPrependTheConfiguredFrontendOrigin_ToAnAppRelativeRoute()
    {
        var sut = CreateSut("https://tests.asklucy.io/");
        var relatedItem = new RelatedItem("AgentExecution", "exec-1", "agent-1");

        var url = sut.BuildAbsolute(AgentExecutionCompleted, relatedItem, Guid.NewGuid());

        url.Should().Be("https://tests.asklucy.io/agents/agent-1/executions/exec-1");
    }

    [Fact]
    public void BuildRelative_ShouldFillTheNotificationIdToken_WithoutARelatedItem()
    {
        var definition = AgentExecutionCompleted with { RouteTemplate = "/notifications/{notificationId}" };
        var sut = CreateSut();
        var notificationId = Guid.NewGuid();

        var route = sut.BuildRelative(definition, relatedItem: null, notificationId);

        route.Should().Be($"/notifications/{notificationId}");
    }

    /// <summary>specs/067 T240 — the knowledge-base document deep link deferred from T092: unlike
    /// documents/memory/settings, <c>/knowledge-bases/{id}</c> was already a plain page route before
    /// this feature, so <c>NotificationItem</c>'s generic <c>navigate(item.action.route)</c> needs no
    /// new frontend code — only this route-resolution check.</summary>
    [Fact]
    public void BuildRelative_ShouldResolveTheKnowledgeBaseIndexingRoute_ToTheKnowledgeBaseDetailPage()
    {
        var completed = NotificationTypeCatalog.Get(NotificationTypeKeys.KnowledgeBaseIndexingCompleted);
        var sut = CreateSut();
        var knowledgeBaseId = Guid.NewGuid().ToString();

        var route = sut.BuildRelative(completed, new RelatedItem("KnowledgeBase", knowledgeBaseId), Guid.NewGuid());

        route.Should().Be($"/knowledge-bases/{knowledgeBaseId}");
    }

    /// <summary>specs/067 US5 — an approval request opens its own approval dialog, so the route carries the approval id.</summary>
    [Fact]
    public void BuildRelative_ShouldFillTheApprovalIdToken_FromTheEventsVariables_UrlEncoded()
    {
        var approval = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowApprovalRequested);
        var sut = CreateSut();
        var variables = new Dictionary<string, string?> { ["approvalId"] = "a b" };

        var route = sut.BuildRelative(approval, new RelatedItem("WorkflowExecution", "exec-1", "wf-1"), Guid.NewGuid(), variables);

        route.Should().Be("/workflows/wf-1/executions/exec-1?approval=a%20b");
    }

    [Fact]
    public void BuildAbsolute_ShouldCarryTheApprovalIdIntoTheEmailLink()
    {
        var approval = NotificationTypeCatalog.Get(NotificationTypeKeys.AgentApprovalRequested);
        var sut = CreateSut();
        var variables = new Dictionary<string, string?> { ["approvalId"] = "appr-1" };

        var url = sut.BuildAbsolute(approval, new RelatedItem("AgentExecution", "exec-1", "agent-1"), Guid.NewGuid(), variables);

        url.Should().Be("https://tests.asklucy.io/agents/agent-1/executions/exec-1?approval=appr-1");
    }

    [Fact]
    public void BuildRelative_ShouldReturnNull_WhenAnApprovalRouteHasNoApprovalId()
    {
        var approval = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowApprovalRequested);
        var sut = CreateSut();

        var route = sut.BuildRelative(approval, new RelatedItem("WorkflowExecution", "exec-1", "wf-1"), Guid.NewGuid());

        route.Should().BeNull("the notification is still shown, just without a link that would open nothing");
    }
}
