using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.TheDigitalCore;

/// <summary>
/// Ask Lucy's implementation of the consumer-driven contract with TheDigitalCore
/// (contracts/thedigitalcore-integration-api.md, ADR-0009). Every call uses the "TheDigitalCore"
/// named <see cref="HttpClient"/> (registered with just <c>BaseAddress</c>/timeout in
/// <c>DependencyInjection.AddInfrastructure</c> — unlike the "Pinecone" named client, the bearer
/// token here can't be set once at client-build time, since TheDigitalCore's service-account JWT
/// expires after ~30 minutes; <see cref="ITheDigitalCoreAuthService"/> is asked for a
/// (cached-until-near-expiry) token on every call instead.
///
/// <para>Endpoint shapes below were corrected against TheDigitalCore's actual
/// <c>ProjectsApiController</c>/<c>ServiceAuthApiController</c> during implementation — the
/// original contract draft assumed a single combined search query and a free-form
/// <c>siteName</c>-only create call; the real API has a dedicated <c>/api/projects/search</c>
/// endpoint (name and near-location are separate query parameters, never combined in one call)
/// and requires a <c>CompanyId</c>/<c>ProjectTypeId</c> on create, which Ask Lucy has no
/// equivalent of and so carries as <see cref="TheDigitalCoreIntegrationOptions.DefaultCompanyId"/>/
/// <see cref="TheDigitalCoreIntegrationOptions.DefaultProjectTypeId"/> instead.</para>
/// </summary>
public sealed class TheDigitalCoreClient(
    IHttpClientFactory httpClientFactory, IOptions<TheDigitalCoreIntegrationOptions> options, ITheDigitalCoreAuthService authService)
    : ITheDigitalCoreClient
{
    public async Task<IReadOnlyList<TheDigitalCoreProjectCandidate>> FindProjectAsync(
        string siteName, decimal? latitude, decimal? longitude, CancellationToken cancellationToken = default)
    {
        // research.md Decision 8 / Clarifications Q3: name first, then geolocation as a secondary
        // signal only when the name search alone is inconclusive (zero or multiple candidates).
        var byName = await SearchAsync($"api/projects/search?name={Uri.EscapeDataString(siteName)}", cancellationToken);
        if (byName.Count == 1)
        {
            return byName;
        }

        if (latitude is null || longitude is null)
        {
            return byName;
        }

        var lat = latitude.Value.ToString(CultureInfo.InvariantCulture);
        var lng = longitude.Value.ToString(CultureInfo.InvariantCulture);
        var byLocation = await SearchAsync($"api/projects/search?latitude={lat}&longitude={lng}&radiusMeters=150", cancellationToken);
        return byLocation.Count > 0 ? byLocation : byName;
    }

    public async Task<string> CreateProjectAsync(string siteName, decimal? latitude, decimal? longitude, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken);
        using var response = await client.PostAsJsonAsync(
            "api/projects",
            new
            {
                name = siteName,
                companyId = options.Value.DefaultCompanyId,
                projectTypeId = options.Value.DefaultProjectTypeId,
                latitude,
                longitude,
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TheDigitalCoreIntegrationException(
                $"TheDigitalCore rejected project creation for '{siteName}' ({(int)response.StatusCode}).");
        }

        var created = await response.Content.ReadFromJsonAsync<ProjectResponse>(cancellationToken);
        return created?.Id.ToString(CultureInfo.InvariantCulture)
            ?? throw new TheDigitalCoreIntegrationException($"TheDigitalCore returned no project id for '{siteName}'.");
    }

    public async Task RelayCategoryScoreResultAsync(string theDigitalCoreProjectId, CategoryScoreResultDto result, CancellationToken cancellationToken = default)
    {
        var maxAttempts = Math.Max(1, options.Value.MaxRetries);
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var client = await CreateClientAsync(cancellationToken);
                using var response = await client.PostAsJsonAsync(
                    $"api/projects/{Uri.EscapeDataString(theDigitalCoreProjectId)}/category-scores",
                    new
                    {
                        category = result.Category,
                        score = result.Score,
                        // The full result (findings/citations/data gaps/review flag) is stored as
                        // opaque JSON, exactly as documented on TheDigitalCore's own
                        // ProjectCategoryScore.FindingsJson — only Category/Score are structured columns there.
                        findingsJson = JsonSerializer.Serialize(result),
                    },
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastFailure = new TheDigitalCoreIntegrationException(
                    $"TheDigitalCore rejected the Category Score Result relay for project '{theDigitalCoreProjectId}' ({(int)response.StatusCode}).");
            }
            catch (HttpRequestException ex)
            {
                lastFailure = ex;
            }
        }

        // SC-007: a failed relay MUST be visibly surfaced, never silently dropped — this exception
        // propagates to RelayCategoryScoreResultCommandHandler, which is responsible for turning it
        // into a user-visible failure (constitution §2.VIII).
        throw new TheDigitalCoreIntegrationException(
            $"Failed to relay the Category Score Result for project '{theDigitalCoreProjectId}' after {maxAttempts} attempt(s).",
            lastFailure);
    }

    private async Task<IReadOnlyList<TheDigitalCoreProjectCandidate>> SearchAsync(string requestUri, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        using var response = await client.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TheDigitalCoreIntegrationException($"TheDigitalCore project search failed ({(int)response.StatusCode}).");
        }

        var candidates = await response.Content.ReadFromJsonAsync<List<ProjectResponse>>(cancellationToken);
        return (candidates ?? [])
            .Select(c => new TheDigitalCoreProjectCandidate(
                c.Id.ToString(CultureInfo.InvariantCulture), c.Name, c.Latitude, c.Longitude,
                c.CompanyId.ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("TheDigitalCore");
        var token = await authService.GetAccessTokenAsync(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Matches TheDigitalCore's ProjectsApiController.ProjectDto exactly (Id is an int there, not
    // a string — Ask Lucy's own model keeps ProjectId as an opaque string throughout, per
    // ITheDigitalCoreClient's contract, converting at this boundary only).
    private sealed record ProjectResponse(
        int Id, string Name, string? Number, string? Description, string? BucketKey,
        decimal? Latitude, decimal? Longitude, int CompanyId, string CompanyName, int ProjectTypeId, string ProjectTypeName);
}
