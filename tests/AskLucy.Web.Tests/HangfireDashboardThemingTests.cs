using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests;

/// <summary>
/// specs/060-hangfire-dashboard-access User Story 2 — Hangfire's <c>DashboardRoutes.Routes.
/// AddStylesheet</c>/<c>AddStylesheetDarkMode</c> (registered once in Program.cs) read these by
/// embedded-resource manifest name, not from a file path or URL, so there's no way to assert
/// their *effect* without a live dashboard render; this confirms the resources Program.cs
/// registers actually exist in the built assembly and aren't empty (research.md Decision 3 —
/// SC-003's visual match is verified manually via quickstart.md).
/// </summary>
public sealed class HangfireDashboardThemingTests
{
    [Theory]
    [InlineData("AskLucy.Web.HangfireTheme.hangfire-theme.css")]
    [InlineData("AskLucy.Web.HangfireTheme.hangfire-theme-dark.css")]
    public void StylesheetResource_ShouldBeEmbeddedAndNonEmpty(string resourceName)
    {
        var assembly = typeof(Program).Assembly;

        assembly.GetManifestResourceNames().Should().Contain(resourceName, "Program.cs registers this resource with DashboardRoutes.Routes");

        using var stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull();
        stream!.Length.Should().BeGreaterThan(0);
    }
}
