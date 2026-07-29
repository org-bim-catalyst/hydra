using System.Net;
using AskLucy.Application.Users;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>
/// FR-019 (T052/G1): no endpoint response may ever include a password hash, security
/// stamp, or concurrency stamp. Verified two ways: structurally (the admin DTO's shape
/// cannot carry those fields, so no future endpoint change can leak them by accident)
/// and via the auth gate (the endpoint itself requires authentication first).
/// </summary>
public sealed class NoSecretLeakageTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly string[] ForbiddenPropertyNames = ["PasswordHash", "SecurityStamp", "ConcurrencyStamp"];

    [Fact]
    public void UserAdminDto_ShouldNotExposeAnyIdentitySecretProperty()
    {
        var propertyNames = typeof(UserAdminDto).GetProperties().Select(p => p.Name);

        propertyNames.Should().NotContain(ForbiddenPropertyNames);
    }

    [Fact]
    public async Task GetAllUsers_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
