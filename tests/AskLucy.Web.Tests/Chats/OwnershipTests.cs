using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Chats;

/// <summary>
/// FR-018/FR-026 (specs/000, specs/002 User Stories 3/4): a user must never modify/delete
/// another user's chat, including reading its detail via the single-chat
/// <c>GET /api/v1/chats/{id}</c> route added in specs/025-chat-configuration-settings (per
/// contracts/api-v1.md, contracts/chats-api.md, and contracts/chat-detail-api.md). The
/// cross-user denial logic itself (User A's request rejected against User B's chat with an
/// authenticated session) is unit-tested at the Application layer —
/// <c>RenameUserChatCommandHandlerTests</c>, <c>DeleteUserChatCommandHandlerTests</c>,
/// <c>ConversationStateCommandsTests</c>, <c>DuplicateUserChatCommandTests</c>,
/// <c>ClearUserChatMessagesCommandTests</c>, <c>PurgeUserChatCommandHandlerTests</c>, and
/// <c>GetChatByIdQueryHandlerTests</c> — where it can be verified without a live database
/// and a second real authenticated user. This class covers the same endpoints' outer auth
/// gate (unauthenticated request → 401), which is what's runnable in this environment
/// without a seeded second user account.
/// </summary>
public sealed class OwnershipTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetChatById_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/chats/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PatchChatById_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PatchAsync(
            $"/api/v1/chats/{Guid.NewGuid()}",
            JsonContent.Create(new { title = "Hijacked" }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteChatById_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.DeleteAsync($"/api/v1/chats/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("restore")]
    [InlineData("pin")]
    [InlineData("unpin")]
    [InlineData("favorite")]
    [InlineData("unfavorite")]
    [InlineData("duplicate")]
    public async Task PostAction_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent(string action)
    {
        var response = await _client.PostAsync($"/api/v1/chats/{Guid.NewGuid()}/actions/{action}", content: null, cancellationToken: TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ClearAction_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync(
            $"/api/v1/chats/{Guid.NewGuid()}/actions/clear",
            JsonContent.Create(new { confirm = true }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PurgeAction_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/chats/{Guid.NewGuid()}/actions/purge")
        {
            Content = JsonContent.Create(new { confirm = true }),
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExportEndpoint_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/chats/{Guid.NewGuid()}/export", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
