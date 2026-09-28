using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Common;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Web.Contracts;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// specs/067-notifications-communication-hub contracts/notifications-api.md, over the real host
/// (<see cref="NotificationsApiFactory"/> — the notification repository, unit of work and realtime
/// publisher are replaced, so nothing here writes to the shared database or SignalR).
///
/// 429 (rate limit) is intentionally not exercised here, for the same reason
/// <see cref="Analytics.AnalyticsControllerTests"/> defers it to quickstart.md: tripping the fixed
/// window in-process would pollute the shared rate-limit partition state for every other test
/// sharing this class's host.
/// </summary>
public sealed class NotificationsEndpointsTests : IClassFixture<NotificationsApiFactory>
{
    // Matches the API's own JSON configuration (JsonEnumSerializationTests.cs) — enums round-trip
    // as strings, not System.Text.Json's numeric-ordinal default.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly NotificationsApiFactory _factory;
    private readonly INotificationRepository _repository;
    private readonly HttpClient _client;

    public NotificationsEndpointsTests(NotificationsApiFactory factory)
    {
        factory.Reset();
        _factory = factory;
        _repository = factory.Repository;
        _client = factory.CreateClient();
    }

    private void Authorize(string userId = "user-1") =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId));

    private static Notification NewCenterNotification(string userId, string title = "Workflow failed", string message = "\"Site survey import\" stopped at step 3.")
    {
        var notification = Notification.Create(
            userId, NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed), NotificationPriority.High,
            title, message, "en", "corr-1", DateTime.UtcNow, showInCenter: true);
        notification.AddDelivery(NotificationDelivery.CreateDelivered(NotificationChannel.InApp, NotificationPriority.High, "en", null, "corr-1", DateTime.UtcNow));
        return notification;
    }

    // --- GET /notifications ---

    [Fact]
    public async Task GetNotifications_ShouldReturn401_WhenNoBearerTokenIsProvided()
    {
        var response = await _client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetNotifications_ShouldReturn200_WithPlainTextTitleAndMessage_UnescapedByHtmlEncoding()
    {
        var notification = NewCenterNotification("user-1", title: "A <b>bold</b> title & more", message: "line1\nline2 <script>");
        _repository.ListAsync("user-1", Arg.Any<IReadOnlyCollection<NotificationCategory>?>(), Arg.Any<NotificationReadState>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Notification>)[notification], (string?)null));
        Authorize();

        var response = await _client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<NotificationListItemDto>>(JsonOptions, TestContext.Current.CancellationToken);
        var item = body!.Items.Should().ContainSingle().Subject;
        item.Title.Should().Be("A <b>bold</b> title & more");
        item.Message.Should().Be("line1\nline2 <script>");
    }

    [Fact]
    public async Task GetNotifications_ShouldReturn400_WhenLimitIsOutOfRange()
    {
        Authorize();

        var response = await _client.GetAsync("/api/v1/notifications?limit=0", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetNotifications_ShouldReturn400_ForAMalformedCursor()
    {
        _repository.ListAsync("user-1", Arg.Any<IReadOnlyCollection<NotificationCategory>?>(), Arg.Any<NotificationReadState>(), "not-a-real-cursor", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<(IReadOnlyList<Notification>, string?)>(_ => throw new ValidationException([new ValidationFailure("cursor", "The cursor is malformed.")]));
        Authorize();

        var response = await _client.GetAsync("/api/v1/notifications?cursor=not-a-real-cursor", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // --- GET /notifications/unread-count ---

    [Fact]
    public async Task GetUnreadCount_ShouldReturn200_WithTheRepositoryCount()
    {
        _repository.CountUnreadAsync("user-1", Arg.Any<CancellationToken>()).Returns(12);
        Authorize();

        var response = await _client.GetAsync("/api/v1/notifications/unread-count", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UnreadNotificationCountResponse>(TestContext.Current.CancellationToken);
        body!.Count.Should().Be(12);
    }

    // --- GET /notifications/{id} ---

    [Fact]
    public async Task GetNotification_ShouldReturn200_WithRelatedItemAvailableFalse_ForADeletedRelatedItem()
    {
        var definition = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);
        var notification = Notification.Create(
            "user-1", definition, NotificationPriority.High, "Workflow failed", "Message", "en", "corr-1", DateTime.UtcNow,
            showInCenter: true, relatedItemType: "WorkflowExecution", relatedItemId: "run-1");
        notification.AddDelivery(NotificationDelivery.CreateDelivered(NotificationChannel.InApp, NotificationPriority.High, "en", null, "corr-1", DateTime.UtcNow));
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        _factory.WorkflowExecutionAccessCheck.CanAccessAsync("user-1", "run-1", Arg.Any<CancellationToken>()).Returns(false);
        Authorize();

        var response = await _client.GetAsync($"/api/v1/notifications/{notification.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<NotificationDetailDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.RelatedItem.Should().NotBeNull();
        body.RelatedItem!.Available.Should().BeFalse();
    }

    [Fact]
    public async Task GetNotification_ShouldReturn404_WhenOwnedByAnotherUser()
    {
        var notification = NewCenterNotification("someone-else");
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        Authorize("user-1");

        var response = await _client.GetAsync($"/api/v1/notifications/{notification.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetNotification_ShouldReturn404_WhenTheIdDoesNotExist()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Notification?)null);
        Authorize();

        var response = await _client.GetAsync($"/api/v1/notifications/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- POST /notifications/{id}/actions/mark-read ---

    [Fact]
    public async Task MarkRead_ShouldReturn204_AndBeIdempotentOnASecondCall()
    {
        var notification = NewCenterNotification("user-1");
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        Authorize();

        var first = await _client.PostAsync($"/api/v1/notifications/{notification.Id}/actions/mark-read", null, TestContext.Current.CancellationToken);
        var second = await _client.PostAsync($"/api/v1/notifications/{notification.Id}/actions/mark-read", null, TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MarkRead_ShouldReturn404_WhenOwnedByAnotherUser()
    {
        var notification = NewCenterNotification("someone-else");
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        Authorize("user-1");

        var response = await _client.PostAsync($"/api/v1/notifications/{notification.Id}/actions/mark-read", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- POST /notifications/actions/mark-all-read ---

    [Fact]
    public async Task MarkAllRead_ShouldReturn200_WithTheUpdatedCount()
    {
        _repository.MarkAllReadAsync("user-1", null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(37);
        Authorize();

        var response = await _client.PostAsync("/api/v1/notifications/actions/mark-all-read", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MarkAllNotificationsReadResponse>(TestContext.Current.CancellationToken);
        body!.Updated.Should().Be(37);
    }

    [Fact]
    public async Task MarkAllRead_ShouldScopeToTheRequestedCategory()
    {
        _repository.MarkAllReadAsync("user-1", NotificationCategory.Document, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(3);
        Authorize();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/notifications/actions/mark-all-read", new MarkAllNotificationsReadRequest(NotificationCategory.Document), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<MarkAllNotificationsReadResponse>(TestContext.Current.CancellationToken);
        body!.Updated.Should().Be(3);
        _ = _repository.Received(1).MarkAllReadAsync("user-1", NotificationCategory.Document, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // --- DELETE /notifications/{id} ---

    [Fact]
    public async Task Delete_ShouldReturn204()
    {
        var notification = NewCenterNotification("user-1");
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        Authorize();

        var response = await _client.DeleteAsync($"/api/v1/notifications/{notification.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_ShouldReturn404_WhenOwnedByAnotherUser()
    {
        var notification = NewCenterNotification("someone-else");
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        Authorize("user-1");

        var response = await _client.DeleteAsync($"/api/v1/notifications/{notification.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ShouldReturn404_WhenAlreadyDeleted()
    {
        // A soft-deleted row never comes back from GetByIdAsync in production (the EF query
        // filter excludes it) — the repository substitute mirrors that instead of returning it.
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Notification?)null);
        Authorize();

        var response = await _client.DeleteAsync($"/api/v1/notifications/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
