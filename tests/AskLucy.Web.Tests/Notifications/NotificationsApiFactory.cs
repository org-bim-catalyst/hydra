using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ClearExtensions;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// One real host for the whole class, with the notification repository, unit of work, realtime
/// publisher and a single access check replaced, so nothing here writes to the shared database or
/// starts a real SignalR/Hangfire round trip. Building a host per test (via
/// <c>WithWebHostBuilder</c> in the constructor, as <see cref="Panels.PanelsControllerTests"/> does
/// for its much smaller surface) started a Hangfire server against the shared test database on
/// every test and stalled the class — see CustomModels.CustomModelsApiFactory, the same fix for the
/// same problem (project memory: webtests-withwebhostbuilder-per-test-stall). xunit runs a class's
/// tests one at a time, so each test calls <see cref="Reset"/> and stubs what it needs.
/// </summary>
public sealed class NotificationsApiFactory : CustomWebApplicationFactory
{
    public INotificationRepository Repository { get; } = Substitute.For<INotificationRepository>();

    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public INotificationRealtimePublisher Realtime { get; } = Substitute.For<INotificationRealtimePublisher>();

    /// <summary>The one <see cref="INotificationAccessCheck"/> the tests need (for "WorkflowExecution"); Phase 4 registers the real ones.</summary>
    public INotificationAccessCheck WorkflowExecutionAccessCheck { get; } = Substitute.For<INotificationAccessCheck>();

    /// <summary>Forgets every stub and received call left by the previous test.</summary>
    public void Reset()
    {
        Repository.ClearSubstitute(ClearOptions.All);
        UnitOfWork.ClearSubstitute(ClearOptions.All);
        Realtime.ClearSubstitute(ClearOptions.All);
        WorkflowExecutionAccessCheck.ClearSubstitute(ClearOptions.All);
        WorkflowExecutionAccessCheck.ItemType.Returns("WorkflowExecution");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton(Repository);
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(UnitOfWork);
            services.RemoveAll<INotificationRealtimePublisher>();
            services.AddSingleton(Realtime);
            services.RemoveAll<INotificationAccessCheck>();
            services.AddSingleton(WorkflowExecutionAccessCheck);
        });
    }
}
