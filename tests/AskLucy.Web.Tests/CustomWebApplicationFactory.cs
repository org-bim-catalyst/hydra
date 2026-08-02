using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AskLucy.Web.Tests;

/// <summary>
/// Boots the real WebAPI host for contract tests that don't require a live database
/// (e.g. confirming the JWT auth gate rejects anonymous requests before any handler,
/// let alone the database, is ever reached — FR-015, User Story 2).
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
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
            });
        });
    }
}
