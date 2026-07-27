using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.WebAPI.Tests.Chats;

/// <summary>
/// FR-018 (T051, User Story 3): a user must never modify/delete another user's chat
/// (there is no single-chat GET endpoint — only the owner-scoped list, per
/// contracts/api-v1.md). The cross-user denial logic itself (User A's request rejected
/// against User B's chat with an authenticated session) is unit-tested at the
/// Application layer in <c>RenameUserChatCommandHandlerTests</c>/
/// <c>DeleteUserChatCommandHandlerTests</c>, where it can be verified without a live
/// database. This class covers the same endpoints' outer auth gate, which is what's
/// runnable in this environment.
/// </summary>
public sealed class OwnershipTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PatchChatById_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PatchAsync(
            $"/api/v1/chats/{Guid.NewGuid()}",
            JsonContent.Create(new { title = "Hijacked" }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteChatById_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.DeleteAsync($"/api/v1/chats/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
