using System.Security.Claims;
using AskLucy.Application.Authentication.Commands.ExternalLogin;
using AskLucy.Application.Options;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.Auth;

/// <summary>
/// Shared OAuth ticket-handling for both Google and Facebook (FR-010 sign-in, FR-034 link).
/// Intercepts the external provider's ticket before the framework signs it into any cookie
/// (via <see cref="TicketReceivedContext.HandleResponse"/>), resolves it through the
/// application layer using only provider-verified claims, and redirects the browser back to
/// the frontend with a one-time completion code — the frontend never receives raw provider
/// claims or an access/refresh token directly in the URL.
///
/// Fixes the vulnerability convergence found in the previous implementation: <c>POST
/// /api/v1/auth/external</c> used to accept <c>provider</c>/<c>providerKey</c>/<c>email</c>
/// directly from an anonymous client request body with zero verification against the actual
/// provider, letting anyone mint a session for an arbitrary account by guessing its email.
/// </summary>
public static class ExternalAuth
{
    public const string TransientScheme = "ExternalTransient";
    public const string ModeKey = "mode";
    public const string LinkUserIdKey = "linkUserId";
    public const string LinkMode = "link";

    public static async Task HandleTicketReceivedAsync(TicketReceivedContext context)
    {
        // Skip the framework's default "sign the ticket into SignInScheme" step — we issue our
        // own one-time code and redirect ourselves instead.
        context.HandleResponse();

        var services = context.HttpContext.RequestServices;
        var mediator = services.GetRequiredService<ISender>();
        var frontendBaseUrl = services.GetRequiredService<IOptions<AppOptions>>().Value.FrontendBaseUrl;

        var principal = context.Principal;
        var providerKey = principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (providerKey is null)
        {
            context.Response.Redirect($"{frontendBaseUrl}/auth/external-complete?error=invalid_response");
            return;
        }

        var email = principal?.FindFirstValue(ClaimTypes.Email);
        var provider = context.Scheme.Name;

        // Google explicitly asserts email verification via its userinfo "email_verified" claim
        // (mapped in Program.cs). Facebook's Graph API only ever returns the email field when
        // it is verified — if unverified, the field is simply absent — so presence alone is
        // sufficient there.
        var emailVerified = provider == GoogleDefaults.AuthenticationScheme
            ? string.Equals(principal?.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)
            : email is not null;

        string? linkToUserId = null;
        if (context.Properties?.Items.TryGetValue(ModeKey, out var mode) == true && mode == LinkMode)
        {
            context.Properties.Items.TryGetValue(LinkUserIdKey, out linkToUserId);
        }

        var code = await mediator.Send(new ProcessExternalLoginCallbackCommand(provider, providerKey, email, emailVerified, linkToUserId));

        context.Response.Redirect(code is not null
            ? $"{frontendBaseUrl}/auth/external-complete?code={Uri.EscapeDataString(code)}"
            : $"{frontendBaseUrl}/auth/external-complete?error=resolution_failed");
    }
}
