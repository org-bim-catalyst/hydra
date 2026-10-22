using System.Net;
using AskLucy.Web.Tests;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Routing;

/// <summary>
/// The SPA fallback must not answer an unmatched /api path with index.html: a 200 that is not JSON turns a wrong URL into a
/// confusing parse error on the client, and hides the real mistake.
/// </summary>
[Collection(DatabaseSerialGroup.Name)]
public sealed class UnknownApiPathTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Get_AnUnmatchedApiPath_Returns404_NotTheSpaHtml()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/this-route-does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("<!DOCTYPE html");
    }
}
