using AskLucy.Application.Abstractions;
using AskLucy.Application.Behaviors;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>T230 — <see cref="AdminViewAuditBehavior{TRequest, TResponse}"/> audits an
/// <see cref="IAuditedAdminView"/> request at most once per admin, per resource, per hour, in its own
/// scope, and after the handler — never before, and never for a request that isn't a view.</summary>
public sealed class AdminViewAuditBehaviorTests : IDisposable
{
    private sealed record ViewRequest(string TargetId) : IAuditedAdminView
    {
        public NotificationAuditAction AuditAction => NotificationAuditAction.DeliveriesViewed;
        public string AuditTargetType => "NotificationDelivery";
        public string AuditTargetId => TargetId;
    }

    private sealed record PlainRequest;

    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly INotificationAuditLogRepository _auditLogs = Substitute.For<INotificationAuditLogRepository>();
    private readonly INotificationAuditWriter _auditWriter = Substitute.For<INotificationAuditWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public void Dispose() => _cache.Dispose();

    private AdminViewAuditBehavior<TRequest, string> CreateSut<TRequest>() where TRequest : notnull
    {
        var services = new ServiceCollection();
        services.AddSingleton(_currentUser);
        services.AddSingleton(_auditLogs);
        services.AddSingleton(_auditWriter);
        services.AddSingleton(_unitOfWork);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new AdminViewAuditBehavior<TRequest, string>(
            scopeFactory, _cache, _time, NullLogger<AdminViewAuditBehavior<TRequest, string>>.Instance);
    }

    [Fact]
    public async Task Handle_ShouldWriteTheAuditRow_AfterTheHandlerSucceeds()
    {
        _currentUser.UserId.Returns("admin-1");
        _auditLogs.ExistsSinceAsync(Arg.Any<NotificationAuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var sut = CreateSut<ViewRequest>();
        var handlerRan = false;

        var response = await sut.Handle(new ViewRequest("delivery-1"), (_) =>
        {
            handlerRan = true;
            return Task.FromResult("ok");
        }, CancellationToken.None);

        response.Should().Be("ok");
        handlerRan.Should().BeTrue();
        _auditWriter.Received(1).Write(NotificationAuditAction.DeliveriesViewed, "NotificationDelivery", "delivery-1", NotificationAuditOutcome.Succeeded, Arg.Any<object?>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldSkipTheHandlerRequirement_ForARequestThatIsNotAnAuditedAdminView()
    {
        var sut = CreateSut<PlainRequest>();

        var response = await sut.Handle(new PlainRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        response.Should().Be("ok");
        _auditWriter.DidNotReceiveWithAnyArgs().Write(default, default!, default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotWriteASecondRow_ForTheSameAdminAndResource_WithinTheAuditWindow()
    {
        _currentUser.UserId.Returns("admin-1");
        _auditLogs.ExistsSinceAsync(Arg.Any<NotificationAuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var sut = CreateSut<ViewRequest>();

        await sut.Handle(new ViewRequest("delivery-1"), _ => Task.FromResult("ok"), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(30));
        await sut.Handle(new ViewRequest("delivery-1"), _ => Task.FromResult("ok"), CancellationToken.None);

        _auditWriter.Received(1).Write(Arg.Any<NotificationAuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationAuditOutcome>(), Arg.Any<object?>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldWriteANewRow_ForADifferentResource_EvenWithinTheSameAdminsWindow()
    {
        _currentUser.UserId.Returns("admin-1");
        _auditLogs.ExistsSinceAsync(Arg.Any<NotificationAuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var sut = CreateSut<ViewRequest>();

        await sut.Handle(new ViewRequest("delivery-1"), _ => Task.FromResult("ok"), CancellationToken.None);
        await sut.Handle(new ViewRequest("delivery-2"), _ => Task.FromResult("ok"), CancellationToken.None);

        _auditWriter.Received(1).Write(Arg.Any<NotificationAuditAction>(), Arg.Any<string>(), "delivery-1", Arg.Any<NotificationAuditOutcome>(), Arg.Any<object?>());
        _auditWriter.Received(1).Write(Arg.Any<NotificationAuditAction>(), Arg.Any<string>(), "delivery-2", Arg.Any<NotificationAuditOutcome>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task Handle_ShouldThrow_AndNeverWrite_WhenTheRequestHasNoActingUser()
    {
        _currentUser.UserId.Returns((string?)null);
        var sut = CreateSut<ViewRequest>();

        var act = () => sut.Handle(new ViewRequest("delivery-1"), _ => Task.FromResult("ok"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _auditWriter.DidNotReceiveWithAnyArgs().Write(default, default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldFallBackToTheAuditTrail_WhenTheCacheMissesButARowAlreadyExists()
    {
        // A cache miss can be a restart or another instance; the durable trail has the final say.
        _currentUser.UserId.Returns("admin-1");
        _auditLogs.ExistsSinceAsync(Arg.Any<NotificationAuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = CreateSut<ViewRequest>();

        await sut.Handle(new ViewRequest("delivery-1"), _ => Task.FromResult("ok"), CancellationToken.None);

        _auditWriter.DidNotReceiveWithAnyArgs().Write(default, default!, default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
