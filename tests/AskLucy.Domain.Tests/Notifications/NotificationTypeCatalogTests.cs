using System.Reflection;
using System.Text.RegularExpressions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Notifications;

public sealed partial class NotificationTypeCatalogTests
{
    /// <summary>The routes reconciled against ClientApp/src/routes/router.tsx (T013, research R11 addendum).</summary>
    private static readonly HashSet<string> ReconciledRoutes =
    [
        "/agents/{parentId}/executions/{id}",
        "/workflows/{parentId}/executions/{id}",
        "/agents/{parentId}/executions/{id}?approval={approvalId}",
        "/workflows/{parentId}/executions/{id}?approval={approvalId}",
        "/documents?documentId={id}",
        "/documents",
        "/knowledge-bases/{id}",
        "/memory?memoryId={id}",
        "/settings?tab=security",
        "/notifications/{notificationId}",
    ];

    private static readonly string[] NotEmitted =
    [
        NotificationTypeKeys.KnowledgeBaseUpdated,
        NotificationTypeKeys.ConversationExportCompleted,
        NotificationTypeKeys.BillingPaymentFailed,
        NotificationTypeKeys.BillingSubscriptionRenewed,
    ];

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]{0,49}$")]
    private static partial Regex TokenName();

    [Fact]
    public void Keys_AreUnique_AndEveryConstantHasARow()
    {
        var keys = NotificationTypeCatalog.All.Select(d => d.Key).ToList();
        keys.Should().OnlyHaveUniqueItems();

        var constants = typeof(NotificationTypeKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        keys.Should().BeEquivalentTo(constants);
        constants.Should().HaveCount(37);
    }

    [Fact]
    public void Get_Throws_ForAnUnknownKey()
    {
        var act = () => NotificationTypeCatalog.Get("no.such.type");

        act.Should().Throw<KeyNotFoundException>();
        NotificationTypeCatalog.TryGet("no.such.type", out _).Should().BeFalse();
    }

    [Fact]
    public void SecurityTypes_MinimizeSensitiveContent()
    {
        NotificationTypeCatalog.All.Where(d => d.Category == NotificationCategory.Security)
            .Should().NotBeEmpty().And.OnlyContain(d => d.MinimizeSensitiveContent);
    }

    [Fact]
    public void AccountTypes_AreEmailOnly_AndHiddenFromTheCenter()
    {
        NotificationTypeCatalog.All.Where(d => d.Category == NotificationCategory.Account)
            .Should().HaveCount(4).And.OnlyContain(d => d.IsEmailOnly && !d.ShowInCenter && d.IsMandatory(NotificationChannel.Email));
    }

    [Fact]
    public void AccountLinkTypes_DeclareTheirLinkKind_AndValidity()
    {
        NotificationTypeCatalog.Get(NotificationTypeKeys.AccountPasswordResetRequested).RequestValidity.Should().Be(TimeSpan.FromMinutes(60));
        NotificationTypeCatalog.Get(NotificationTypeKeys.AccountEmailConfirmationRequested).SensitiveLinkKind.Should().Be(SensitiveLinkKind.EmailConfirmation);
        NotificationTypeCatalog.Get(NotificationTypeKeys.AccountEmailChangeRequested).SensitiveLinkKind.Should().Be(SensitiveLinkKind.EmailChange);
    }

    [Fact]
    public void DeferredTypes_AreDefinedButNotEmitted_AndEveryOtherTypeIsEmitted()
    {
        foreach (var definition in NotificationTypeCatalog.All)
        {
            definition.IsEmitted.Should().Be(!NotEmitted.Contains(definition.Key), definition.Key);
        }
    }

    [Fact]
    public void DeclaredVariableNames_AreValidTokens()
    {
        NotificationTypeCatalog.All.SelectMany(d => d.AllVariables)
            .Should().OnlyContain(v => TokenName().IsMatch(v.Name));
    }

    [Fact]
    public void RouteTemplates_UseOnlyReconciledRoutes()
    {
        foreach (var definition in NotificationTypeCatalog.All.Where(d => d.RouteTemplate is not null))
        {
            ReconciledRoutes.Should().Contain(definition.RouteTemplate!, definition.Key);
        }
    }

    [Fact]
    public void NoTypeAllowsExternalLinks()
    {
        NotificationTypeCatalog.All.Should().OnlyContain(d => !d.AllowsExternalLink);
    }

    [Fact]
    public void ApprovalTypes_AreFlagged()
    {
        NotificationTypeCatalog.All.Where(d => d.IsApproval).Select(d => d.Key)
            .Should().BeEquivalentTo(NotificationTypeKeys.AgentApprovalRequested, NotificationTypeKeys.WorkflowApprovalRequested);
    }

    [Theory]
    [InlineData(NotificationCategory.Security, NotificationChannel.Email, true)]
    [InlineData(NotificationCategory.Security, NotificationChannel.InApp, true)]
    [InlineData(NotificationCategory.Account, NotificationChannel.Email, true)]
    [InlineData(NotificationCategory.System, NotificationChannel.InApp, true)]
    [InlineData(NotificationCategory.System, NotificationChannel.Email, false)]
    [InlineData(NotificationCategory.Workflow, NotificationChannel.Email, false)]
    public void LockedPairs_MatchTheMandatoryRules(NotificationCategory category, NotificationChannel channel, bool locked)
    {
        NotificationTypeCatalog.IsLocked(category, channel).Should().Be(locked);
        NotificationTypeCatalog.IsConfigurable(category, channel).Should().Be(!locked);
    }

    [Fact]
    public void DeferredCategories_AreNotConfigurable()
    {
        NotificationTypeCatalog.IsConfigurable(NotificationCategory.Billing, NotificationChannel.InApp).Should().BeFalse();
        NotificationTypeCatalog.IsConfigurable(NotificationCategory.Conversation, NotificationChannel.InApp).Should().BeFalse();
    }

    [Fact]
    public void EmittedCategories_LeaveOutTheDeferredOnes()
    {
        var categories = NotificationTypeCatalog.EmittedCategories();

        categories.Should().NotContain([NotificationCategory.Billing, NotificationCategory.Conversation]);
        categories.Should().Contain([NotificationCategory.Security, NotificationCategory.Workflow, NotificationCategory.System]);
    }

    [Theory]
    [InlineData(NotificationCategory.Workflow, NotificationChannel.Email, true)]
    [InlineData(NotificationCategory.Billing, NotificationChannel.Email, false)]
    public void IsUsed_IsTrueOnlyWhenAnEmittedTypeUsesTheChannel(NotificationCategory category, NotificationChannel channel, bool used)
    {
        NotificationTypeCatalog.IsUsed(category, channel).Should().Be(used);
    }

    [Fact]
    public void DefaultEnabled_FollowsTheOptionalTypesDefaults_ForEveryConfigurablePair()
    {
        foreach (var category in NotificationTypeCatalog.EmittedCategories())
        {
            foreach (var channel in Enum.GetValues<NotificationChannel>().Where(c => NotificationTypeCatalog.IsConfigurable(category, c)))
            {
                var expected = NotificationTypeCatalog.All.Any(d => d.IsEmitted && d.Category == category
                    && d.Channels.TryGetValue(channel, out var state) && state == ChannelDefault.On);
                NotificationTypeCatalog.DefaultEnabled(category, channel).Should().Be(expected, $"{category}/{channel}");
            }
        }
    }
}
