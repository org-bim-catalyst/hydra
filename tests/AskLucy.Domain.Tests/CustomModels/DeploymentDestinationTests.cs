using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.CustomModels;

/// <summary>specs/072 research D5 — destination and repository-path containment (FR-005, FR-005a, FR-006, FR-015).</summary>
public sealed class DeploymentDestinationTests
{
    private static readonly IReadOnlyList<string> Prefixes = ["Models", "App_Data/Models"];

    [Theory]
    [InlineData("Models/supertonic-3", "Models/supertonic-3")]
    [InlineData("App_Data/Models/whisper/large", "App_Data/Models/whisper/large")]
    [InlineData("Models//x/", "Models/x")]
    [InlineData("models/x", "models/x")]
    [InlineData("Models/supertonic%2D3", "Models/supertonic-3")]
    public void TryCreate_ShouldAccept_AndCanonicalise_PathsBelowAnAllowedPrefix(string raw, string expected)
    {
        var ok = DeploymentDestination.TryCreate(raw, Prefixes, out var destination, out var error);

        ok.Should().BeTrue(error);
        error.Should().BeNull();
        destination!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("Models")]
    [InlineData("Models/")]
    [InlineData("App_Data")]
    [InlineData("wwwroot/x")]
    [InlineData("ModelsX/y")]
    [InlineData("../x")]
    [InlineData("Models/../x")]
    [InlineData("Models/./x")]
    [InlineData("Models/%2e%2e/x")]
    [InlineData("Models/%252e%252e/x")]
    [InlineData("/Models/x")]
    [InlineData("\\Models\\x")]
    [InlineData("Models\\x")]
    [InlineData("C:/x")]
    [InlineData("Models/x/CON")]
    [InlineData("Models/x/nul.txt")]
    [InlineData("Models/COM1/x")]
    [InlineData("Models/x.")]
    [InlineData("Models/x ")]
    [InlineData("Models/ x")]
    [InlineData("Models/x\u2024y")]
    [InlineData("Models/x\uFF0Ey")]
    [InlineData("Models/x\u3002y")]
    [InlineData("Models/a:b")]
    [InlineData("Models/a*b")]
    [InlineData("Models/a?b")]
    [InlineData("Models/a<b")]
    [InlineData("Models/a|b")]
    [InlineData("Models/a\"b")]
    [InlineData("Models/a\tb")]
    [InlineData("Models/a\u0001b")]
    [InlineData("Models/%00x")]
    public void TryCreate_ShouldReject_UnsafeOrReservedDestinations(string raw)
    {
        var ok = DeploymentDestination.TryCreate(raw, Prefixes, out var destination, out var error);

        ok.Should().BeFalse();
        destination.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCreate_ShouldReject_Null()
    {
        DeploymentDestination.TryCreate(null, Prefixes, out var destination, out var error).Should().BeFalse();
        destination.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCreate_ShouldReject_APathLongerThanTheColumn()
    {
        var raw = "Models/" + new string('a', DeploymentDestination.MaxLength);

        DeploymentDestination.TryCreate(raw, Prefixes, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCreate_ShouldMatchPrefixes_IgnoringCaseAndSurroundingSlashes()
    {
        DeploymentDestination.TryCreate("MODELS/x", ["/Models/"], out var destination, out _).Should().BeTrue();
        destination!.Value.Should().Be("MODELS/x");
    }

    [Fact]
    public void TryCreate_ShouldNameTheTraversalSegment_InTheError()
    {
        DeploymentDestination.TryCreate("Models/../x", Prefixes, out _, out var error);

        error.Should().Contain("..");
    }

    [Theory]
    [InlineData("onnx/model.onnx", "Models/supertonic-3/onnx/model.onnx")]
    [InlineData("README.md", "Models/supertonic-3/README.md")]
    [InlineData("a/b/c.json", "Models/supertonic-3/a/b/c.json")]
    public void TryCombine_ShouldAccept_SafeRepositoryPaths(string relativePath, string expected)
    {
        var destination = Create("Models/supertonic-3");

        var ok = destination.TryCombine(relativePath, out var combined, out var error);

        ok.Should().BeTrue(error);
        combined.Should().Be(expected);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../b")]
    [InlineData("./a")]
    [InlineData("web.config")]
    [InlineData("sub/Web.Config")]
    [InlineData("APP_OFFLINE.HTM")]
    [InlineData("deep/er/app_offline.htm")]
    [InlineData("a\\b")]
    [InlineData("/a")]
    [InlineData("a//b")]
    [InlineData("")]
    [InlineData("a/CON")]
    [InlineData("a:b")]
    [InlineData("a.")]
    public void TryCombine_ShouldReject_UnsafeOrReservedRepositoryPaths(string relativePath)
    {
        var destination = Create("Models/supertonic-3");

        var ok = destination.TryCombine(relativePath, out var combined, out var error);

        ok.Should().BeFalse();
        combined.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("web.config", true)]
    [InlineData("sub/Web.Config", true)]
    [InlineData("APP_OFFLINE.HTM", true)]
    [InlineData("web.config.bak", false)]
    [InlineData("onnx/model.onnx", false)]
    public void IsReservedFileName_ShouldFlag_TheFilesThatWouldTakeTheSiteDown(string relativePath, bool expected) =>
        DeploymentDestination.IsReservedFileName(relativePath).Should().Be(expected);

    [Theory]
    [InlineData("Models/a", "Models/a", true)]
    [InlineData("Models/a", "models/A", true)]
    [InlineData("Models/a", "Models/a/b", true)]
    [InlineData("Models/a/b", "Models/a", true)]
    [InlineData("Models/a", "Models/ab", false)]
    [InlineData("Models/ab", "Models/a", false)]
    [InlineData("Models/a", "App_Data/Models/a", false)]
    public void Overlaps_ShouldCompareOnSegmentBoundaries_IgnoringCase(string left, string right, bool expected) =>
        DeploymentDestination.Overlaps(left, right).Should().Be(expected);

    private static DeploymentDestination Create(string raw)
    {
        DeploymentDestination.TryCreate(raw, Prefixes, out var destination, out var error).Should().BeTrue(error);
        return destination!;
    }
}
