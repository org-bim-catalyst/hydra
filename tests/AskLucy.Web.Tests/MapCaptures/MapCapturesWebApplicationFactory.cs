using System.Globalization;
using AskLucy.Application.Locations;
using AskLucy.Application.MapCaptures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace AskLucy.Web.Tests.MapCaptures;

/// <summary>
/// specs/083-viewer-map-capture T014 — one real host for the whole class, with the geocoder and
/// renderer replaced so no test touches the network. Never <c>WithWebHostBuilder</c> per test:
/// that builds a new host and Hangfire per test and stalls the suite.
/// </summary>
public class MapCapturesWebApplicationFactory : CustomWebApplicationFactory
{
    public const string ApiKey = "test-map-capture-key";
    public const string SecondApiKey = "test-map-capture-key-2";
    public const string CallerName = "test-pipeline";
    public const string SecondCallerName = "test-pipeline-2";
    public const int RateLimitPerMinute = 1000;

    public IGeocodingProvider Geocoding { get; } = Substitute.For<IGeocodingProvider>();

    public IMapImageRenderer Renderer { get; } = Substitute.For<IMapImageRenderer>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["MapCapture:ApiKeys:0:Name"] = CallerName,
                ["MapCapture:ApiKeys:0:Key"] = ApiKey,
                ["MapCapture:ApiKeys:1:Name"] = SecondCallerName,
                ["MapCapture:ApiKeys:1:Key"] = SecondApiKey,
                ["MapCapture:RateLimitPerMinute"] =
                    RateLimitPerMinute.ToString(CultureInfo.InvariantCulture),
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGeocodingProvider>();
            services.AddSingleton(Geocoding);
            services.RemoveAll<IMapImageRenderer>();
            services.AddSingleton(Renderer);
        });
    }
}

/// <summary>Two requests per minute per caller, so the third trip is observable.</summary>
public sealed class RateLimitedMapCapturesFactory : MapCapturesWebApplicationFactory
{
    public new const int RateLimitPerMinute = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["MapCapture:RateLimitPerMinute"] =
                    RateLimitPerMinute.ToString(CultureInfo.InvariantCulture),
            }));
    }
}
