using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Mcp;

public sealed class McpEndpointValidatorTests
{
    private readonly McpEndpointValidator _validator = new(Substitute.For<ILogger<McpEndpointValidator>>());

    [Theory]
    [InlineData("https://10.0.0.5/")]
    [InlineData("https://172.16.0.1/")]
    [InlineData("https://172.31.255.255/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://127.0.0.1/")]
    public async Task ValidateAsync_ShouldRejectPrivateOrLoopback_ByDefault(string endpoint)
    {
        var result = await _validator.ValidateAsync(endpoint, allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.RejectedPrivateOrLoopback);
    }

    [Theory]
    [InlineData("https://169.254.1.1/")]
    [InlineData("https://169.254.169.254/")]
    public async Task ValidateAsync_ShouldRejectLinkLocalAndCloudMetadata_ByDefault(string endpoint)
    {
        var result = await _validator.ValidateAsync(endpoint, allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.RejectedLinkLocalOrCloudMetadata);
    }

    [Fact]
    public async Task ValidateAsync_ShouldAllow_WhenOverrideIsExplicitlySet()
    {
        var result = await _validator.ValidateAsync("https://10.0.0.5/", allowOverride: true);

        result.Should().Be(McpEndpointValidationResult.Allowed);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectInsecureScheme()
    {
        var result = await _validator.ValidateAsync("ftp://8.8.8.8/", allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.RejectedInsecureScheme);
    }

    [Fact]
    public async Task ValidateAsync_ShouldAllow_PublicAddress()
    {
        // 8.8.8.8 is a public, well-known address (Google Public DNS) — used as a literal IP so
        // this test never performs an actual DNS lookup.
        var result = await _validator.ValidateAsync("https://8.8.8.8/", allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.Allowed);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectUnresolvable_WhenEndpointIsNotAnAbsoluteUri()
    {
        var result = await _validator.ValidateAsync("not-a-uri", allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.RejectedUnresolvable);
    }

    // spec.md Security Tests "full range matrix" — boundary cases just outside each rejected
    // range, proving the check is exact (not accidentally over- or under-inclusive).
    [Theory]
    [InlineData("https://172.15.255.255/")] // one below 172.16.0.0/12
    [InlineData("https://172.32.0.0/")] // one above 172.16.0.0/12
    [InlineData("https://11.0.0.0/")] // outside 10.0.0.0/8
    [InlineData("https://192.169.0.0/")] // outside 192.168.0.0/16
    [InlineData("https://169.253.255.255/")] // one below 169.254.0.0/16
    [InlineData("https://169.255.0.0/")] // one above 169.254.0.0/16
    public async Task ValidateAsync_ShouldAllow_AddressesJustOutsideAPrivateOrLinkLocalRange(string endpoint)
    {
        var result = await _validator.ValidateAsync(endpoint, allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.Allowed);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectIPv6Loopback()
    {
        var result = await _validator.ValidateAsync("https://[::1]/", allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.RejectedPrivateOrLoopback);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectIPv6UniqueLocalAddresses()
    {
        // fc00::/7 — the IPv6 analogue of RFC1918 private ranges.
        var result = await _validator.ValidateAsync("https://[fd12:3456:789a::1]/", allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.RejectedPrivateOrLoopback);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectIPv6LinkLocalAddresses()
    {
        var result = await _validator.ValidateAsync("https://[fe80::1]/", allowOverride: false);

        result.Should().Be(McpEndpointValidationResult.RejectedLinkLocalOrCloudMetadata);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRevalidateFromScratch_OnEveryCall_WithNoInternalCaching()
    {
        // DNS-rebinding structural guarantee: the validator holds no cache field at all — every
        // call performs a fresh DNS resolution, so a hostname that resolves differently between
        // two calls (as in a rebinding attack) is independently re-checked both times, never
        // trusting an earlier call's result.
        var first = await _validator.ValidateAsync("https://8.8.8.8/", allowOverride: false);
        var second = await _validator.ValidateAsync("https://10.0.0.5/", allowOverride: false);

        first.Should().Be(McpEndpointValidationResult.Allowed);
        second.Should().Be(McpEndpointValidationResult.RejectedPrivateOrLoopback);
    }
}
