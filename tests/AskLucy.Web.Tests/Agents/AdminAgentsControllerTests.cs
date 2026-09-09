using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Agents;

/// <summary>
/// specs/047 FR-005 — the admin-only System Agents view's outer auth gate. Same no-live-database,
/// self-signed-JWT pattern as <see cref="Ai.AdminAiProvidersControllerTests"/>: role evaluation is
/// purely a function of the token's claims, independent of any data access, so a 200 here proves
/// authorization let the request through, not that data was returned correctly (that is
/// <c>GetSystemAgentsQueryHandlerTests</c>'s job).
/// </summary>
public sealed class AdminAgentsControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Path = "/api/v1/admin/agents/system";

    [Fact]
    public async Task GetSystemAgents_ShouldReturn401_WhenAnonymous()
    {
        var response = await _client.GetAsync(Path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSystemAgents_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync(Path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task GetSystemAgents_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string role)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", role));

        var response = await _client.GetAsync(Path, TestContext.Current.CancellationToken);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the caller through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
