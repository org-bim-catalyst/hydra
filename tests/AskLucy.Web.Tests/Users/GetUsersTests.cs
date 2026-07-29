using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>
/// FR-009/010/011 (specs/001-admin-dashboard). Filter/sort/pagination correctness itself is
/// covered by <c>GetUsersQueryHandlerTests</c> (unit) and <c>UserAdminRepositorySearchTests</c>
/// (integration, against a real database) — this proves the endpoint's authorization gate and
/// that it accepts the query-string shape without erroring on validation.
/// </summary>
public sealed class GetUsersTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users?search=jane&sortBy=createdAtUtc&sortDescending=true&page=2&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturn200_WithDefaultQueryParameters_WhenCallerIsAdmin()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldReturn400_WhenSortByIsInvalid()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users?sortBy=notARealColumn");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
