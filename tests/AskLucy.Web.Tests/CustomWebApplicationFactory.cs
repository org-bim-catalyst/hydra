using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AskLucy.Web.Tests;

/// <summary>
/// Boots the real WebAPI host for contract tests that don't require a live database
/// (e.g. confirming the JWT auth gate rejects anonymous requests before any handler,
/// let alone the database, is ever reached — FR-015, User Story 2).
/// </summary>
// Not sealed: ForgotPasswordEndpointTests derives from it to stub the password email job.
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // CI has no LocalDB instance provisioned, so it points this at the same
                // real, persistent test SQL Server instance AskLucy.Persistence.Tests uses
                // (see PersistenceTestFixture) via the same environment variable, serialized
                // against it by the same `backend-tests-shared-db` concurrency group in
                // ci.yml. Falls back to LocalDB for local development machines that already
                // have it provisioned and don't set the variable.
                ["ConnectionStrings:DefaultConnection"] =
                    Environment.GetEnvironmentVariable("PERSISTENCE_TESTS_CONNECTION_STRING")
                    ?? "Server=(localdb)\\mssqllocaldb;Database=AskLucyTests;Trusted_Connection=True;",
                ["Jwt:Issuer"] = "https://tests.asklucy.io",
                ["Jwt:Audience"] = "https://tests.asklucy.io",
                ["Jwt:SigningKey"] = "test-signing-key-not-for-production-use-minimum-32-chars",
                ["OpenAI:ApiKey"] = "test-key",
                ["Smtp:Host"] = "test-smtp.invalid",
                ["FileStorage:RootPath"] = "App_Data/test-avatars",
                ["App:FrontendBaseUrl"] = "https://tests.asklucy.io",
                ["CookiePolicy:CurrentVersion"] = "2026-07-30.1",
                ["CookiePolicy:EffectiveAtUtc"] = "2026-07-30T00:00:00Z",
                // The content root is src/AskLucy.Web, the same as a local dev server's; without
                // this the startup sweep (specs/072) would delete that server's in-flight folders.
                ["CustomModels:TempDirectory"] = Path.Combine(Path.GetTempPath(), "asklucy-web-tests", "custom-models"),

                // specs/067: every host runs a notification delivery worker against the one shared database,
                // so any host may send a delivery a test queued. Retries are due at once, and the send limiter
                // is out of the way, so a scripted outage doesn't leave a delivery waiting minutes.
                ["Notifications:Retry:DelaysMinutes:0"] = "0",
                ["Notifications:Retry:CriticalDelaysSeconds:0"] = "0",
                ["Notifications:Email:MaxPerMinute"] = "1000000",

                // Every host's log lines are kept in CapturedLogSink for tests that assert what was never logged.
                ["Serilog:Using:0"] = "AskLucy.Web.Tests",
                ["Serilog:WriteTo:0:Name"] = "CapturedLogs",
            });
        });

        // Every host sends through the one process-wide fake SMTP server (see ScriptableEmailSender), never
        // the real SMTP sender against test-smtp.invalid. Registered with ConfigureTestServices, which runs
        // after the app's own registrations (a plain ConfigureServices runs before them, and the app's would
        // win). A derived factory that captures mail replaces it the same way, after this.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Notifications.ScriptableEmailSender.Shared);

            // Every host answers "yes" for the item ids a test put on NotificationAccessAllowList (see there) and asks the real check for the rest.
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(INotificationAccessCheck) && d.ImplementationType is not null).ToList())
            {
                services.Remove(descriptor);
                var implementation = descriptor.ImplementationType!;
                services.Add(new ServiceDescriptor(
                    typeof(INotificationAccessCheck),
                    sp => new NotificationAccessAllowList.Wrapper((INotificationAccessCheck)ActivatorUtilities.CreateInstance(sp, implementation)),
                    descriptor.Lifetime));
            }
        });
    }
}
