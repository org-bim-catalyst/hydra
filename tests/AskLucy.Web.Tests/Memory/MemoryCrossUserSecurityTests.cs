using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Memory;

/// <summary>
/// tasks.md T102 (spec.md FR-027, SC-005): a user must never access, modify, or delete another
/// user's memories or Projects. Mirrors <c>AskLucy.Web.Tests.Chats.OwnershipTests</c>'s
/// established scope boundary for this environment: the cross-user denial logic itself (User A's
/// authenticated request rejected — as 404, never 403, per <c>MemoryOwnershipGuard</c>/
/// <c>ProjectOwnershipGuard</c> — against User B's memory/Project) is unit-tested at the
/// Application layer, where it can be verified without a live database and a second real
/// authenticated user — see <c>MemoryOwnershipTests</c>, <c>ProjectScopedMemoryTests</c>,
/// <c>ProjectDeletionCascadeTests</c>, and <c>ResolveConflictTests</c>. This class covers every
/// Memory/Projects endpoint's outer auth gate (unauthenticated request → 401), which is what's
/// runnable in this environment without a seeded second user account.
/// </summary>
public sealed class MemoryCrossUserSecurityTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListMemories_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/memories");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMemoryById_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/memories/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EditMemory_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/memories/{Guid.NewGuid()}", new { content = "Hijacked" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteMemory_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.DeleteAsync($"/api/v1/memories/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task ApprovalAction_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent(string action)
    {
        var response = await _client.PostAsync($"/api/v1/memories/{Guid.NewGuid()}/actions/{action}", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResolveConflict_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/memories/{Guid.NewGuid()}/actions/resolve-conflict", new { resolution = "KeepExisting" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ClearAllMemories_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/memories/actions/clear-all", new { confirm = true });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestExport_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync("/api/v1/memories/actions/export", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetExportStatus_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/memories/exports/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPreferences_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/memories/preferences");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdatePreferences_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/memories/preferences", new { memoryEnabled = false, categories = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListNotifications_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/memories/notifications");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MarkNotificationRead_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/memories/notifications/{Guid.NewGuid()}/actions/mark-read", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DownloadExportContent_ShouldReturn403_WhenSignatureIsMissing()
    {
        // Intentionally [AllowAnonymous] (a signed link is its own authorization, mirroring
        // DocumentsController.DownloadContent) — so the security boundary here is signature
        // validation, not the JWT auth gate. An unsigned/forged request must still be denied.
        var response = await _client.GetAsync("/api/v1/memories/exports/content?fileName=someone-elses-export.json&exp=0&sig=forged");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListProjects_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/projects");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateProject_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/projects", new { name = "Hijacked Project" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RenameProject_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PutAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}", new { name = "Hijacked" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteProject_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.DeleteAsync($"/api/v1/projects/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AssignConversationToProject_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/chats/{Guid.NewGuid()}/project", new { projectId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
