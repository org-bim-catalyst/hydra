using System.Net;
using System.Security.Claims;
using AskLucy.Web.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

public sealed class RateLimitPartitionsTests
{
    [Fact]
    public void UserOrClientKey_KeysAnAuthenticatedCallerOnTheirUserId_NotTheirAddress()
    {
        // Shaped like a real access token after inbound mapping: a user id and an email, no name claim.
        var context = ContextFrom("203.0.113.7", new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Email, "a@b.test"));

        RateLimitPartitions.UserOrClientKey(context).Should().Be("user-1");
    }

    [Fact]
    public void UserOrClientKey_GivesTwoUsersBehindOneAddressSeparateKeys()
    {
        var first = ContextFrom("203.0.113.7", new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var second = ContextFrom("203.0.113.7", new Claim(ClaimTypes.NameIdentifier, "user-2"));

        RateLimitPartitions.UserOrClientKey(first).Should().NotBe(RateLimitPartitions.UserOrClientKey(second));
    }

    [Fact]
    public void UserOrClientKey_FallsBackToTheClientAddress_ForAnAnonymousCaller()
    {
        RateLimitPartitions.UserOrClientKey(ContextFrom("203.0.113.7")).Should().Be("203.0.113.7");
    }

    [Fact]
    public void UserOrClientKey_IsAnonymous_WhenThereIsNeitherAUserNorAnAddress()
    {
        RateLimitPartitions.UserOrClientKey(ContextFrom(address: null)).Should().Be("anonymous");
    }

    private static DefaultHttpContext ContextFrom(string? address, params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length == 0 ? null : "Bearer")),
        };
        context.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        return context;
    }
}
