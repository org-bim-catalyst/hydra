using System.Reflection;
using AskLucy.Web.Auth;
using AskLucy.Web.Controllers.v1;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>
/// SC-010: every admin action is gated by either a <see cref="RequirePermissionAttribute"/> or
/// the reserved <c>AdministratorOrSuperUser</c> policy — never left on the bare
/// <c>[Authorize]</c> any-authenticated-user check. Reflection-based so a new admin controller/
/// action added later without an authorization attribute fails this test immediately rather than
/// silently shipping unauthorized (research.md Decision 4).
/// </summary>
public sealed class AdminEndpointPermissionCoverageTests
{
    private const string ReservedPolicy = "AdministratorOrSuperUser";

    public static TheoryData<MethodInfo> AdminActions()
    {
        var data = new TheoryData<MethodInfo>();

        foreach (var controllerType in typeof(AdminRolesController).Assembly.GetTypes())
        {
            if (!typeof(ControllerBase).IsAssignableFrom(controllerType) || controllerType.IsAbstract)
            {
                continue;
            }

            var routeAttribute = controllerType.GetCustomAttribute<RouteAttribute>();
            var isAdminController = routeAttribute?.Template?.Contains("api/v1/admin", StringComparison.OrdinalIgnoreCase) == true;
            var isUsersController = controllerType == typeof(UsersController);

            if (!isAdminController && !isUsersController)
            {
                continue;
            }

            foreach (var method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var hasHttpMethodAttribute = method.GetCustomAttributes().Any(a => a is IActionHttpMethodProvider);
                if (!hasHttpMethodAttribute)
                {
                    continue;
                }

                // UsersController's non-admin self-service actions (GetMe/UpdateMe/etc.) aren't
                // admin actions at all — only the ones this feature's research.md Decision 5
                // table actually maps are in scope.
                if (isUsersController && !IsAdminUserAction(method.Name))
                {
                    continue;
                }

                data.Add(method);
            }
        }

        return data;
    }

    private static bool IsAdminUserAction(string methodName) =>
        methodName is nameof(UsersController.GetAll) or nameof(UsersController.UpdateUser) or nameof(UsersController.Lock)
            or nameof(UsersController.Unlock) or nameof(UsersController.ChangeRole) or nameof(UsersController.ForceReset2fa)
            or nameof(UsersController.DeleteUser)
            // specs/056-bulk-select-all
            or nameof(UsersController.GetBulkEligibleIds) or nameof(UsersController.BulkLock) or nameof(UsersController.BulkUnlock)
            or nameof(UsersController.BulkForceReset2fa) or nameof(UsersController.BulkDelete);

    [Theory]
    [MemberData(nameof(AdminActions))]
    public void EveryAdminAction_CarriesEitherRequirePermissionOrTheReservedPolicy(MethodInfo action)
    {
        var declaringType = action.DeclaringType!;

        var hasRequirePermission =
            action.GetCustomAttribute<RequirePermissionAttribute>() is not null ||
            declaringType.GetCustomAttribute<RequirePermissionAttribute>() is not null;

        var hasReservedPolicy =
            action.GetCustomAttribute<AuthorizeAttribute>()?.Policy == ReservedPolicy ||
            declaringType.GetCustomAttribute<AuthorizeAttribute>()?.Policy == ReservedPolicy;

        (hasRequirePermission || hasReservedPolicy).Should().BeTrue(
            $"{declaringType.Name}.{action.Name} must carry [RequirePermission] or the reserved AdministratorOrSuperUser policy");
    }
}
