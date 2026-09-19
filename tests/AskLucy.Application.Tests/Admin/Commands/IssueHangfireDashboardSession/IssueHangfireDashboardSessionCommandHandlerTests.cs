using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Admin.Commands.IssueHangfireDashboardSession;
using AskLucy.Application.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Admin.Commands.IssueHangfireDashboardSession;

/// <summary>specs/060-hangfire-dashboard-access — the token-minting handler behind "Jobs".</summary>
public sealed class IssueHangfireDashboardSessionCommandHandlerTests
{
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private IssueHangfireDashboardSessionCommandHandler CreateHandler() =>
        new(_tokenService, _currentUser, Substitute.For<ILogger<IssueHangfireDashboardSessionCommandHandler>>());

    [Fact]
    public async Task Handle_ShouldMintATokenCarryingThePurposeClaimAndTheConfiguredLifetime()
    {
        _currentUser.UserId.Returns("user-1");
        _currentUser.IsInRole(PrivilegedRoleNames.Administrator).Returns(true);
        _tokenService.GenerateAccessToken(Arg.Any<string>(), Arg.Any<IEnumerable<Claim>>(), Arg.Any<TimeSpan>())
            .Returns(new AccessTokenResult("minted-token", DateTime.UtcNow.AddMinutes(30)));

        var result = await CreateHandler().Handle(new IssueHangfireDashboardSessionCommand(), CancellationToken.None);

        result.AccessToken.Should().Be("minted-token");
        result.Lifetime.Should().Be(HangfireDashboardSessionClaims.Lifetime);

        _tokenService.Received(1).GenerateAccessToken(
            "user-1",
            Arg.Is<IEnumerable<Claim>>(claims => claims != null && claims.Any(c =>
                c.Type == HangfireDashboardSessionClaims.PurposeClaimType &&
                c.Value == HangfireDashboardSessionClaims.PurposeClaimValue)),
            HangfireDashboardSessionClaims.Lifetime);
    }

    [Theory]
    [InlineData(true, false, PrivilegedRoleNames.Administrator)]
    [InlineData(false, true, PrivilegedRoleNames.SuperUser)]
    public async Task Handle_ShouldRecordWhicheverPrivilegedRoleTheCallerHolds(bool isAdmin, bool isSuperUser, string expectedRole)
    {
        _currentUser.UserId.Returns("user-1");
        _currentUser.IsInRole(PrivilegedRoleNames.Administrator).Returns(isAdmin);
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(isSuperUser);
        _tokenService.GenerateAccessToken(Arg.Any<string>(), Arg.Any<IEnumerable<Claim>>(), Arg.Any<TimeSpan>())
            .Returns(new AccessTokenResult("minted-token", DateTime.UtcNow.AddMinutes(30)));

        await CreateHandler().Handle(new IssueHangfireDashboardSessionCommand(), CancellationToken.None);

        _tokenService.Received(1).GenerateAccessToken(
            Arg.Any<string>(),
            Arg.Is<IEnumerable<Claim>>(claims => claims != null && claims.Any(c => c.Type == ClaimTypes.Role && c.Value == expectedRole)),
            Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenNoAuthenticatedUserIsPresent()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(new IssueHangfireDashboardSessionCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
