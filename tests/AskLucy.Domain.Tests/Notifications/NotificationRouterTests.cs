using AskLucy.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Notifications;

/// <summary>The FR-003 decision table (SC-009) and the R28 preference precedence.</summary>
public sealed class NotificationRouterTests
{
    private static readonly RecipientRoutingState Verified = new(HasVerifiedEmail: true);
    private static readonly HashSet<NotificationChannel> AllChannels = [NotificationChannel.InApp, NotificationChannel.Email];

    private static IReadOnlyList<ChannelDecision> Route(
        string type,
        IReadOnlyCollection<PreferenceOverride>? overrides = null,
        RecipientRoutingState? recipient = null,
        IReadOnlySet<NotificationChannel>? channels = null,
        bool isCritical = false) =>
        NotificationRouter.Route(NotificationTypeCatalog.Get(type), recipient ?? Verified, overrides ?? [], channels ?? AllChannels, isCritical);

    private static ChannelDecision For(IReadOnlyList<ChannelDecision> decisions, NotificationChannel channel) =>
        decisions.Single(d => d.Channel == channel);

    [Fact]
    public void MandatoryPairs_IgnorePreferenceOverrides()
    {
        var overrides = new[]
        {
            new PreferenceOverride(NotificationCategory.Security, NotificationChannel.Email, false),
            new PreferenceOverride(NotificationCategory.Security, NotificationChannel.InApp, false),
        };

        var decisions = Route(NotificationTypeKeys.SecurityTwoFactorDisabled, overrides);

        decisions.Should().OnlyContain(d => d.Deliver);
    }

    [Fact]
    public void OptionalPairs_FallBackToCatalogueDefaults_WhenNothingIsSaved()
    {
        For(Route(NotificationTypeKeys.WorkflowExecutionFailed), NotificationChannel.Email).Deliver.Should().BeTrue();

        var completed = For(Route(NotificationTypeKeys.WorkflowExecutionCompleted), NotificationChannel.Email);
        completed.Deliver.Should().BeFalse();
        completed.SkipReason.Should().Be(DeliverySkipReason.PreferenceDisabled);
    }

    [Fact]
    public void SavedCategoryOverride_SwitchesEveryOptionalTypeInTheCategory_On()
    {
        var overrides = new[] { new PreferenceOverride(NotificationCategory.Workflow, NotificationChannel.Email, true) };

        For(Route(NotificationTypeKeys.WorkflowExecutionCompleted, overrides), NotificationChannel.Email).Deliver.Should().BeTrue();
        For(Route(NotificationTypeKeys.WorkflowExecutionStarted, overrides), NotificationChannel.Email).Deliver.Should().BeTrue();
        For(Route(NotificationTypeKeys.WorkflowExecutionFailed, overrides), NotificationChannel.Email).Deliver.Should().BeTrue();
    }

    [Fact]
    public void SavedCategoryOverride_SwitchesEveryOptionalTypeInTheCategory_Off()
    {
        var overrides = new[] { new PreferenceOverride(NotificationCategory.Workflow, NotificationChannel.Email, false) };

        var failed = For(Route(NotificationTypeKeys.WorkflowExecutionFailed, overrides), NotificationChannel.Email);
        failed.Deliver.Should().BeFalse();
        failed.SkipReason.Should().Be(DeliverySkipReason.PreferenceDisabled);
        For(Route(NotificationTypeKeys.WorkflowApprovalRequested, overrides), NotificationChannel.Email).Deliver.Should().BeFalse();
    }

    [Fact]
    public void Override_ForAnotherCategoryOrChannel_HasNoEffect()
    {
        var overrides = new[]
        {
            new PreferenceOverride(NotificationCategory.Agent, NotificationChannel.Email, false),
            new PreferenceOverride(NotificationCategory.Workflow, NotificationChannel.InApp, true),
        };

        For(Route(NotificationTypeKeys.WorkflowExecutionFailed, overrides), NotificationChannel.Email).Deliver.Should().BeTrue();
    }

    [Fact]
    public void Override_NeverAddsAChannelTheTypeDoesNotUse()
    {
        var overrides = new[] { new PreferenceOverride(NotificationCategory.Account, NotificationChannel.InApp, true) };

        var decisions = Route(NotificationTypeKeys.AccountPasswordResetRequested, overrides);

        decisions.Select(d => d.Channel).Should().Equal(NotificationChannel.Email);
    }

    [Fact]
    public void EmailOnlyTypes_NeverProduceInApp()
    {
        foreach (var definition in NotificationTypeCatalog.All.Where(d => d.IsEmailOnly))
        {
            Route(definition.Key).Should().NotContain(d => d.Channel == NotificationChannel.InApp, definition.Key);
        }
    }

    [Fact]
    public void DisabledOrUnregisteredChannel_IsSkippedAsChannelDisabled_EvenWhenMandatory()
    {
        var inAppOnly = new HashSet<NotificationChannel> { NotificationChannel.InApp };

        var decision = For(Route(NotificationTypeKeys.SecurityTwoFactorEnabled, channels: inAppOnly), NotificationChannel.Email);

        decision.Deliver.Should().BeFalse();
        decision.SkipReason.Should().Be(DeliverySkipReason.ChannelDisabled);
    }

    [Fact]
    public void Announcement_EmailsOnlyWhenCritical()
    {
        var normal = For(Route(NotificationTypeKeys.SystemAnnouncementPublished), NotificationChannel.Email);
        normal.Deliver.Should().BeFalse();
        normal.SkipReason.Should().Be(DeliverySkipReason.NotCritical);

        For(Route(NotificationTypeKeys.SystemAnnouncementPublished, isCritical: true), NotificationChannel.Email).Deliver.Should().BeTrue();
    }

    [Fact]
    public void CriticalAnnouncement_RespectsSystemEmailOff_ButAlwaysReachesInApp()
    {
        var overrides = new[] { new PreferenceOverride(NotificationCategory.System, NotificationChannel.Email, false) };

        var decisions = Route(NotificationTypeKeys.SystemAnnouncementPublished, overrides, isCritical: true);

        For(decisions, NotificationChannel.Email).SkipReason.Should().Be(DeliverySkipReason.PreferenceDisabled);
        For(decisions, NotificationChannel.InApp).Deliver.Should().BeTrue();
    }

    [Fact]
    public void MissingVerifiedAddress_SkipsEmail()
    {
        var decision = For(Route(NotificationTypeKeys.SecurityPasswordChanged, recipient: new RecipientRoutingState(false)), NotificationChannel.Email);

        decision.Deliver.Should().BeFalse();
        decision.SkipReason.Should().Be(DeliverySkipReason.NoVerifiedAddress);
    }

    [Fact]
    public void PreferenceSkip_TakesPrecedenceOverMissingAddress()
    {
        var decision = For(Route(NotificationTypeKeys.WorkflowExecutionCompleted, recipient: new RecipientRoutingState(false)), NotificationChannel.Email);

        decision.SkipReason.Should().Be(DeliverySkipReason.PreferenceDisabled);
    }
}
