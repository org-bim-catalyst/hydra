using System.Net;
using System.Net.Http.Json;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>
/// FR-017/FR-019 (T053): the admin update endpoint must never persist client-supplied
/// fields beyond an explicit allow-list — closing the legacy mass-assignment
/// vulnerability where the entire <c>ApplicationUser</c> entity was bound from the
/// request body and passed to <c>UpdateAsync</c> as-is.
/// </summary>
public sealed class UpdateUserOverpostingTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public void UpdateUserRequest_ShouldOnlyExposeAllowListedFields()
    {
        var propertyNames = typeof(UpdateUserRequest).GetProperties().Select(p => p.Name);

        propertyNames.Should().BeEquivalentTo(["FirstName", "LastName"],
            "the request contract itself must make it structurally impossible to bind passwordHash, role, id, etc. " +
            "from the client — not merely rely on the handler choosing not to read them");
    }

    [Fact]
    public async Task UpdateUser_ShouldIgnoreUnknownFields_AndStillRequireAuthentication()
    {
        // Even a request smuggling extra fields the DTO doesn't declare must still hit
        // the auth gate first — defense in depth on top of the structural guarantee above.
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/users/some-user-id")
        {
            Content = JsonContent.Create(new
            {
                firstName = "Eve",
                lastName = "Attacker",
                passwordHash = "AAAAAAAA==",
                role = "Administrator",
                id = "victim-user-id",
            }),
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
