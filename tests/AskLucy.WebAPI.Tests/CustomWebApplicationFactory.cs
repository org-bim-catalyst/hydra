using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AskLucy.WebAPI.Tests;

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
                ["ConnectionStrings:ChatGPT_ClientContextConnection"] =
                    "Server=(localdb)\\mssqllocaldb;Database=AskLucyTests;Trusted_Connection=True;",
                ["Jwt:Issuer"] = "https://tests.asklucy.io",
                ["Jwt:Audience"] = "https://tests.asklucy.io",
                ["Jwt:SigningKey"] = "test-signing-key-not-for-production-use-minimum-32-chars",
                ["OpenAI:ApiKey"] = "test-key",
                ["SendGrid:ApiKey"] = "test-key",
                ["FileStorage:RootPath"] = "App_Data/test-avatars",
                ["App:FrontendBaseUrl"] = "https://tests.asklucy.io",
            });
        });
    }
}
