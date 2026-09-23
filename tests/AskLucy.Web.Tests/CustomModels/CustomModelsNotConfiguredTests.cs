using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AskLucy.Web.Tests.CustomModels;

/// <summary>
/// specs/072 T066 (FR-019) — a server with no <c>Ftp</c> section still boots, and says so instead of
/// failing. <see cref="CustomWebApplicationFactory"/> configures no <c>Ftp</c> section, and the real
/// configuration-backed provider is left in place.
/// </summary>
public sealed class CustomModelsNotConfiguredTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CustomModelsNotConfiguredTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));
    }

    [Fact]
    public async Task DeploymentStatus_ShouldReportNotConfigured()
    {
        var response = await _client.GetAsync("/api/v1/admin/custom-models/deployment-status", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("isConfigured").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Submit_ShouldReturn400DeploymentNotConfigured()
    {
        var response = await _client.PostAsync(
            "/api/v1/admin/custom-models",
            JsonContent.Create(new SubmitCustomModelRequest("https://huggingface.co/Supertone/supertonic-3", "Models/supertonic-3", null)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("type").GetString().Should().Be("https://hydra.bimcatalyst.com/problems/deployment-not-configured");
    }
}
