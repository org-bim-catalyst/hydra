using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Commands.ArchiveTemplateVersion;
using AskLucy.Application.Notifications.Commands.CreateTemplateDraft;
using AskLucy.Application.Notifications.Commands.PublishTemplateVersion;
using AskLucy.Application.Notifications.Commands.SendTemplateTest;
using AskLucy.Application.Notifications.Commands.UpdateTemplateDraft;
using AskLucy.Application.Notifications.Queries.GetNotificationTemplate;
using AskLucy.Application.Notifications.Queries.PreviewTemplateVersion;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>T173 (specs/067 US7) — draft-only edits, If-Match concurrency, publish/archive rules, and variable validation.</summary>
public sealed class NotificationTemplateHandlerTests
{
    private const string AdminId = "admin-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Token = [1, 2, 3];

    private readonly INotificationTemplateRepository _templates = Substitute.For<INotificationTemplateRepository>();
    private readonly INotificationAuditWriter _audit = Substitute.For<INotificationAuditWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly INotificationRecipientDirectory _directory = Substitute.For<INotificationRecipientDirectory>();
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly FakeTimeProvider _time = new(Now);

    public NotificationTemplateHandlerTests()
    {
        _currentUser.UserId.Returns(AdminId);
    }

    private static NotificationTemplateContent Email(string heading = "Hello {{ workflowName }}", string subject = "{{ workflowName }} failed") => new()
    {
        Subject = subject,
        Heading = heading,
        BodyParagraphs = ["It failed: {{ failureSummary }}"],
        SafetyNote = "If this wasn't you, ignore this.",
        ActionLabel = "Open",
    };

    /// <summary>A template for a shipped default (an emitted type) with a published v1, or an unshipped one with only a draft.</summary>
    private NotificationTemplate Template(bool shippedDefault = true)
    {
        var type = shippedDefault ? NotificationTypeKeys.WorkflowExecutionFailed : NotificationTypeKeys.BillingPaymentFailed;
        var template = NotificationTemplate.Create(type, NotificationChannel.Email, "en", "Test", Now.UtcDateTime);
        var v1 = template.AddDraft(Email(), Now.UtcDateTime);
        v1.RowVersion = Token;
        if (shippedDefault)
        {
            template.Publish(v1.Id, "system:template-seeder", Now.UtcDateTime);
        }

        _templates.GetWithVersionsAsync(template.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(template);
        return template;
    }

    private UpdateTemplateDraftCommandHandler Update() => new(_templates, _audit, _unitOfWork);

    private PublishTemplateVersionCommandHandler Publish() => new(_templates, _audit, _unitOfWork, _currentUser, _time);

    private ArchiveTemplateVersionCommandHandler Archive() => new(_templates, _audit, _unitOfWork, _currentUser, _time);

    private CreateTemplateDraftCommandHandler Create() => new(_templates, _audit, _unitOfWork, _time);

    private static async Task<NotificationTemplateConflictException> ConflictOf(Func<Task> action) =>
        (await action.Should().ThrowAsync<NotificationTemplateConflictException>()).Which;

    [Fact]
    public async Task CreateDraft_ShouldNumberTheNextVersion_AndAudit()
    {
        var template = Template();

        var dto = await Create().Handle(new CreateTemplateDraftCommand(template.Id, Email()), TestContext.Current.CancellationToken);

        dto.VersionNumber.Should().Be(2);
        dto.Status.Should().Be(TemplateVersionStatus.Draft);
        dto.UsedVariables.Should().Contain(["workflowName", "failureSummary"]);
        _audit.Received(1).Write(NotificationAuditAction.TemplateDraftSaved, "NotificationTemplateVersion", dto.Id.ToString(), NotificationAuditOutcome.Succeeded, Arg.Any<object?>(), Arg.Any<string?>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateDraft_ShouldCopyAnExistingVersion_WhenNoFieldsAreSent()
    {
        var template = Template();
        var v1 = template.Versions.Single();

        var dto = await Create().Handle(new CreateTemplateDraftCommand(template.Id, null, v1.Id), TestContext.Current.CancellationToken);

        dto.Heading.Should().Be(v1.Heading);
        dto.BodyParagraphs.Should().Equal(v1.BodyParagraphs);
    }

    [Fact]
    public async Task UpdateDraft_ShouldRefuseAPublishedVersion_WithVersionNotDraft()
    {
        var template = Template();
        var published = template.PublishedVersion!;
        published.RowVersion = Token;

        var ex = await ConflictOf(() => Update().Handle(new UpdateTemplateDraftCommand(template.Id, published.Id, Email(), Token), TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(TemplateConflictReason.VersionNotDraft);
    }

    [Fact]
    public async Task UpdateDraft_ShouldRefuseAStaleRowVersion_WithConcurrencyConflict()
    {
        var template = Template();
        var draft = template.AddDraft(Email(), Now.UtcDateTime);
        draft.RowVersion = [9, 9];

        var ex = await ConflictOf(() => Update().Handle(new UpdateTemplateDraftCommand(template.Id, draft.Id, Email(), Token), TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(TemplateConflictReason.ConcurrencyConflict);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateDraft_ShouldSaveWithTheCallersRowVersion()
    {
        var template = Template();
        var draft = template.AddDraft(Email(), Now.UtcDateTime);
        draft.RowVersion = Token;

        var dto = await Update().Handle(new UpdateTemplateDraftCommand(template.Id, draft.Id, Email(heading: "Changed {{ workflowName }}"), Token), TestContext.Current.CancellationToken);

        dto.Heading.Should().Be("Changed {{ workflowName }}");
        _templates.Received(1).ExpectRowVersion(draft, Arg.Is<byte[]>(b => b.SequenceEqual(Token)));
    }

    [Theory]
    [InlineData("Hello {{ nobody }}", "nobody")]
    [InlineData("Hello {{ workflowName", "{{")]
    [InlineData("Visit https://evil.example now", "Links and HTML")]
    [InlineData("Hello <b>{{ workflowName }}</b>", "Links and HTML")]
    public async Task UpdateDraft_ShouldRejectBadText_NamingTheProblem(string heading, string expected)
    {
        var template = Template();
        var draft = template.AddDraft(Email(), Now.UtcDateTime);
        draft.RowVersion = Token;

        var act = () => Update().Handle(new UpdateTemplateDraftCommand(template.Id, draft.Id, Email(heading: heading), Token), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotificationTemplateRejectedException>()).Which.Message.Should().Contain(expected);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_ShouldArchiveThePreviousVersion_AndAuditBothNumbers()
    {
        var template = Template();
        var v1 = template.PublishedVersion!;
        var draft = template.AddDraft(Email(heading: "New {{ workflowName }}"), Now.UtcDateTime);
        draft.RowVersion = Token;

        var dto = await Publish().Handle(new PublishTemplateVersionCommand(template.Id, draft.Id, Token), TestContext.Current.CancellationToken);

        dto.Status.Should().Be(TemplateVersionStatus.Published);
        v1.Status.Should().Be(TemplateVersionStatus.Archived);
        template.PublishedVersionId.Should().Be(draft.Id);
        _audit.Received(1).Write(
            NotificationAuditAction.TemplateVersionPublished, "NotificationTemplateVersion", draft.Id.ToString(), NotificationAuditOutcome.Succeeded,
            Arg.Is<object?>(d => d!.ToString()!.Contains("previousVersionNumber = 1") && d.ToString()!.Contains("newVersionNumber = 2")), Arg.Any<string?>());
    }

    [Fact]
    public async Task Publish_ShouldRefuseANonDraft()
    {
        var template = Template();
        var published = template.PublishedVersion!;
        published.RowVersion = Token;

        var ex = await ConflictOf(() => Publish().Handle(new PublishTemplateVersionCommand(template.Id, published.Id, Token), TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(TemplateConflictReason.VersionNotDraft);
    }

    [Fact]
    public async Task Archive_ShouldRefuseTheLastPublishedVersionOfAShippedDefault()
    {
        var template = Template(shippedDefault: true);
        var published = template.PublishedVersion!;

        var ex = await ConflictOf(() => Archive().Handle(new ArchiveTemplateVersionCommand(template.Id, published.Id, Token), TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(TemplateConflictReason.LastPublishedDefault);
        template.PublishedVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task Archive_ShouldAllowADraft_AndAuditIt()
    {
        var template = Template();
        var draft = template.AddDraft(Email(), Now.UtcDateTime);
        draft.RowVersion = Token;

        var dto = await Archive().Handle(new ArchiveTemplateVersionCommand(template.Id, draft.Id, Token), TestContext.Current.CancellationToken);

        dto.Status.Should().Be(TemplateVersionStatus.Archived);
        _audit.Received(1).Write(NotificationAuditAction.TemplateVersionArchived, "NotificationTemplateVersion", draft.Id.ToString(), NotificationAuditOutcome.Succeeded, Arg.Any<object?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Archive_ShouldRefuseAnAlreadyArchivedVersion()
    {
        var template = Template();
        var draft = template.AddDraft(Email(), Now.UtcDateTime);
        draft.RowVersion = Token;
        template.Archive(draft.Id, AdminId, Now.UtcDateTime);

        var ex = await ConflictOf(() => Archive().Handle(new ArchiveTemplateVersionCommand(template.Id, draft.Id, Token), TestContext.Current.CancellationToken));

        ex.Reason.Should().Be(TemplateConflictReason.VersionArchived);
    }

    [Fact]
    public async Task GetTemplate_ShouldFlagShippedDefaults_AndListTheDeclaredVariables()
    {
        var template = Template();
        _directory.GetAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, NotificationRecipientInfo>());

        var dto = await new GetNotificationTemplateQueryHandler(_templates, _directory).Handle(new GetNotificationTemplateQuery(template.Id), TestContext.Current.CancellationToken);

        dto.IsShippedDefault.Should().BeTrue();
        dto.Versions.Should().ContainSingle().Which.CreatedBy.Should().BeNull();
        dto.DeclaredVariables.Where(v => !v.IsStandard).Select(v => v.Name).Should().Equal("workflowName", "failureSummary");
    }

    [Fact]
    public async Task Preview_ShouldRenderWithSamples_AndTheFixedSampleLink()
    {
        var template = Template();
        var renderer = Substitute.For<INotificationTemplatePreviewRenderer>();
        renderer.DirectionOf("en").Returns("ltr");
        IReadOnlyDictionary<string, string?>? seen = null;
        renderer.PreviewEmail(Arg.Any<NotificationTypeDefinition>(), "en", Arg.Any<NotificationTemplateVersion>(), Arg.Do<IReadOnlyDictionary<string, string?>>(v => seen = v))
            .Returns(new RenderedEmail("Subject", "<p>Hi</p>", "Hi", Guid.NewGuid(), "en"));

        var dto = await new PreviewTemplateVersionQueryHandler(_templates, renderer).Handle(
            new PreviewTemplateVersionQuery(template.Id, template.Versions.Single().Id, new Dictionary<string, string?> { ["workflowName"] = "Demo", ["nope"] = "x" }), TestContext.Current.CancellationToken);

        dto.Html.Should().Be("<p>Hi</p>");
        seen!["workflowName"].Should().Be("Demo");
        seen["failureSummary"].Should().Be("an unexpected error");
        seen["actionUrl"].Should().Be(TemplateSamples.SampleLink);
        seen.Should().NotContainKey("nope");
    }

    [Fact]
    public async Task SendTest_ShouldPublishToTheCallerOnly_AsTemplateTest()
    {
        var template = Template();
        var version = template.Versions.Single();
        _directory.GetAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, NotificationRecipientInfo>
        {
            [AdminId] = new(AdminId, "Ada Lovelace", "ada@example.com", EmailConfirmed: true, IsActive: true),
        });
        NotificationRequest? sent = null;
        _publisher.When(p => p.Publish(Arg.Any<NotificationRequest>())).Do(c => sent = c.Arg<NotificationRequest>());

        var result = await new SendTemplateTestCommandHandler(_templates, _directory, _publisher, _audit, _unitOfWork, _currentUser, _time)
            .Handle(new SendTemplateTestCommand(template.Id, version.Id), TestContext.Current.CancellationToken);

        sent!.Type.Should().Be(NotificationTypeKeys.TemplateTest);
        sent.Recipient.Should().Be(new NotificationRecipient.User(AdminId));
        sent.Variables[TemplateSamples.TestVersionVariable].Should().Be(version.Id.ToString());
        sent.Variables["workflowName"].Should().Be("your workflow");
        result.SentTo.Should().Be("a•••@example.com");
        _audit.Received(1).Write(NotificationAuditAction.TemplateTestSent, "NotificationTemplateVersion", version.Id.ToString(), NotificationAuditOutcome.Succeeded, Arg.Any<object?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task SendTest_ShouldRefuse_WhenTheAdminHasNoVerifiedAddress()
    {
        var template = Template();
        _directory.GetAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, NotificationRecipientInfo>
        {
            [AdminId] = new(AdminId, "Ada", "ada@example.com", EmailConfirmed: false, IsActive: true),
        });

        var act = () => new SendTemplateTestCommandHandler(_templates, _directory, _publisher, _audit, _unitOfWork, _currentUser, _time)
            .Handle(new SendTemplateTestCommand(template.Id, template.Versions.Single().Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotificationTemplateRejectedException>();
        _publisher.DidNotReceive().Publish(Arg.Any<NotificationRequest>());
    }
}
