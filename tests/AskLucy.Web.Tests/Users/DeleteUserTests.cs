using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>
/// FR-016/FR-022 (specs/001-admin-dashboard). The "soft-deleted users disappear from
/// GET /api/v1/users" guarantee is proven at the Persistence layer by
/// <c>ApplicationUserQueryFilterTests</c> (the global query filter every list read goes
/// through) — not duplicated here as a live end-to-end assertion.
/// </summary>
public sealed class DeleteUserTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.DeleteAsync("/api/v1/users/some-other-user");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturn403_WhenTargetingSelf()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.DeleteAsync("/api/v1/users/admin-1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldPassAuthorization_WhenCallerHasAnAdminRoleAndTargetsSomeoneElse()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.DeleteAsync("/api/v1/users/some-other-user");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Me_ShouldStillRouteToDeleteMyAccount_NotDeleteUser()
    {
        // Regression guard: "me" is a literal route segment competing with the new
        // "{userId}" parameterized DELETE — ASP.NET Core routing must still prefer the
        // literal match, not the newly added admin action.
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.DeleteAsync("/api/v1/users/me");

        // DeleteMyAccountCommand requires a Password field in the body; an empty body
        // fails validation (400), never the admin-only 403 DeleteUser would produce for a
        // non-admin caller — proving "me" still resolves to the self-service action.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
