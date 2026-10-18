using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using AskLucy.Application.Ai.Deprecation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/069 FR-018/FR-020b: everyone who administers AI providers is told, and only a Super User may opt out.</summary>
public sealed class AdminRecipientResolverTests
{
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _assignments = Substitute.For<IRoleAssignmentRepository>();
    private readonly AdminRecipientResolver _resolver;

    public AdminRecipientResolverTests()
    {
        _resolver = new AdminRecipientResolver(_roles, _assignments);
        _roles.ListByPermissionAsync("admin.ai-providers.manage", Arg.Any<CancellationToken>()).Returns([]);
        _roles.GetByNormalizedNameAsync("ADMINISTRATOR", Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);
        _roles.GetByNormalizedNameAsync("SUPER USER", Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);
        _assignments.ListUserIdsByRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private void Role(string id, string normalizedName)
    {
        if (normalizedName == "ADMINISTRATOR")
        {
            _roles.GetByNormalizedNameAsync("ADMINISTRATOR", Arg.Any<CancellationToken>())
                .Returns(TestRole(id, "Administrator"));
        }

        if (normalizedName == "SUPER USER")
        {
            _roles.GetByNormalizedNameAsync("SUPER USER", Arg.Any<CancellationToken>())
                .Returns(TestRole(id, "Super User"));
        }
    }

    private static RoleRecord TestRole(string id, string name) =>
        new(id, name, name, true, PermissionSet.Full, 0, null, $"stamp-{id}");

    [Fact]
    public async Task AUserHoldingOnlySuperUser_MayOptOut()
    {
        Role("admin-role", "ADMINISTRATOR");
        Role("su-role", "SUPER USER");
        _assignments.ListUserIdsByRoleAsync("su-role", Arg.Any<CancellationToken>()).Returns(["su-only"]);

        var recipients = await _resolver.ResolveAsync(TestContext.Current.CancellationToken);

        recipients.Mandatory.Should().BeEmpty();
        recipients.Optional.Should().ContainSingle().Which.Should().Be("su-only");
    }

    [Fact]
    public async Task AUserHoldingBoth_IsTreatedAsAdministrator()
    {
        Role("admin-role", "ADMINISTRATOR");
        Role("su-role", "SUPER USER");
        _assignments.ListUserIdsByRoleAsync("admin-role", Arg.Any<CancellationToken>()).Returns(["both"]);
        _assignments.ListUserIdsByRoleAsync("su-role", Arg.Any<CancellationToken>()).Returns(["both"]);

        var recipients = await _resolver.ResolveAsync(TestContext.Current.CancellationToken);

        recipients.Mandatory.Should().ContainSingle().Which.Should().Be("both");
        recipients.Optional.Should().BeEmpty();
    }

    [Fact]
    public async Task ACustomRoleWithThePermission_CannotOptOut()
    {
        Role("admin-role", "ADMINISTRATOR");
        _roles.ListByPermissionAsync("admin.ai-providers.manage", Arg.Any<CancellationToken>())
            .Returns([TestRole("custom-role", "Provider Ops")]);
        _assignments.ListUserIdsByRoleAsync("custom-role", Arg.Any<CancellationToken>()).Returns(["custom-user"]);

        var recipients = await _resolver.ResolveAsync(TestContext.Current.CancellationToken);

        recipients.Mandatory.Should().Contain("custom-user");
        recipients.Optional.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSameUserInTwoQualifyingRoles_IsListedOnce()
    {
        Role("admin-role", "ADMINISTRATOR");
        _roles.ListByPermissionAsync("admin.ai-providers.manage", Arg.Any<CancellationToken>())
            .Returns([TestRole("custom-role", "Provider Ops")]);
        _assignments.ListUserIdsByRoleAsync("admin-role", Arg.Any<CancellationToken>()).Returns(["user-1"]);
        _assignments.ListUserIdsByRoleAsync("custom-role", Arg.Any<CancellationToken>()).Returns(["user-1"]);

        var recipients = await _resolver.ResolveAsync(TestContext.Current.CancellationToken);

        recipients.Mandatory.Should().ContainSingle().Which.Should().Be("user-1");
    }

    [Fact]
    public async Task NoAdmins_YieldsNoRecipients()
    {
        var recipients = await _resolver.ResolveAsync(TestContext.Current.CancellationToken);

        recipients.Mandatory.Should().BeEmpty();
        recipients.Optional.Should().BeEmpty();
    }
}
