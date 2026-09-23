using System.Net;
using AskLucy.Infrastructure.CustomModels.HuggingFace;
using FluentAssertions;

namespace AskLucy.Infrastructure.Tests.CustomModels;

/// <summary>specs/072 T056. The "HuggingFace" client connects only to public addresses, whatever DNS says.</summary>
public sealed class SafeConnectCallbackTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.5")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public void IsPublicAddress_RefusesNonPublicAddresses(string address) =>
        SafeConnectCallback.IsPublicAddress(IPAddress.Parse(address)).Should().BeFalse();

    [Theory]
    [InlineData("18.164.52.75")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2600:9000:2000::1")]
    [InlineData("::ffff:18.164.52.75")]
    public void IsPublicAddress_AllowsPublicAddresses(string address) =>
        SafeConnectCallback.IsPublicAddress(IPAddress.Parse(address)).Should().BeTrue();
}
