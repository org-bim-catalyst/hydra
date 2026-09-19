using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.ChangeEmail;
using AskLucy.Application.Authentication.Commands.ChangePassword;
using AskLucy.Application.Authentication.Commands.ConfirmEmail;
using AskLucy.Application.Authentication.Commands.ExternalLogin;
using AskLucy.Application.Authentication.Commands.Login;
using AskLucy.Application.Authentication.Commands.LoginTwoFactor;
using AskLucy.Application.Authentication.Commands.Logout;
using AskLucy.Application.Authentication.Commands.Refresh;
using AskLucy.Application.Authentication.Commands.Register;
using AskLucy.Application.Authentication.Commands.RemoveExternalLogin;
using AskLucy.Application.Authentication.Commands.RequestAccountSupport;
using AskLucy.Application.Authentication.Commands.RequestPasswordReset;
using AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;
using AskLucy.Application.Authentication.Commands.ResetPassword;
using AskLucy.Application.Authentication.Commands.TwoFactor;
using AskLucy.Application.Authentication.Queries.GetExternalLogins;
using AskLucy.Application.Authentication.Queries.GetPasswordStatus;
using AskLucy.Application.Authentication.Queries.GetSession;
using AskLucy.Application.Authentication.Queries.ValidatePasswordResetToken;
using AskLucy.Infrastructure.Auth;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Preserves FR-009/FR-010/FR-011 through a JWT-based API, per contracts/api-v1.md.</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    ISender mediator,
    IExternalLoginCodeStore externalLoginCodeStore,
    IAuthenticationSchemeProvider schemeProvider,
    IOptions<JwtOptions> jwtOptions)
    : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RegisterCommand(request.Email, request.Password, request.FirstName, request.LastName), cancellationToken);
        return result.Outcome == AuthOutcome.Success
            ? Ok(ToResponse(result))
            : Problem(title: "Registration failed", detail: string.Join(' ', result.Errors ?? []), statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("login/2fa")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> LoginTwoFactor(LoginTwoFactorRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new LoginTwoFactorCommand(request.UserId, request.Code, request.IsRecoveryCode), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.Name, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
        {
            return Problem(title: "No refresh token present", statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await mediator.Send(new RefreshCommand(refreshToken), cancellationToken);
        if (result.Outcome != AuthOutcome.Success)
        {
            ClearAuthCookies();
        }

        return ToActionResult(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshTokenCookie.Name, out var refreshToken) && !string.IsNullOrEmpty(refreshToken))
        {
            await mediator.Send(new LogoutCommand(refreshToken), cancellationToken);
        }

        ClearAuthCookies();
        return NoContent();
    }

    /// <summary>
    /// Lightweight session check backing the frontend's route guards (specs — cookie-based
    /// session), decoupled from the 15-minute access token so navigation doesn't bounce a
    /// user to /login while their 14-day refresh token is still valid.
    /// </summary>
    [HttpGet("session")]
    [AllowAnonymous]
    public async Task<ActionResult<SessionResponse>> GetSession(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.Name, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
        {
            return Problem(title: "No active session", statusCode: StatusCodes.Status401Unauthorized);
        }

        var session = await mediator.Send(new GetSessionQuery(refreshToken), cancellationToken);
        return session.Authenticated
            ? Ok(new SessionResponse(true, session.UserId, session.Roles, session.Permissions))
            : Problem(title: "Session expired", statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Anonymous entry point for first-time/returning social sign-in (FR-010). Redirects the
    /// browser to the real provider; the provider then redirects to our callback, which is
    /// handled entirely by <see cref="ExternalAuth.HandleTicketReceivedAsync"/> (registered in
    /// Program.cs), not by an action here.
    /// </summary>
    [HttpGet("external/{provider}/challenge")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalChallenge(string provider)
    {
        var scheme = await ResolveConfiguredSchemeAsync(provider);
        if (scheme is null)
        {
            return Problem(title: "External login provider is not available", statusCode: StatusCodes.Status400BadRequest);
        }

        return Challenge(new AuthenticationProperties(), scheme);
    }

    /// <summary>
    /// Entry point for linking an additional provider to the current account (FR-034). Reached
    /// via a plain top-level browser navigation (no Authorization header possible), so identity
    /// is carried by a single-use <paramref name="ticket"/> obtained beforehand from
    /// <see cref="IssueExternalLoginLinkTicket"/> over an authenticated request, never by a
    /// bearer token in the URL.
    /// </summary>
    [HttpGet("external/{provider}/link")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLink(string provider, [FromQuery] string ticket)
    {
        var scheme = await ResolveConfiguredSchemeAsync(provider);
        if (scheme is null)
        {
            return Problem(title: "External login provider is not available", statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = externalLoginCodeStore.TryConsume(ticket);
        if (userId is null)
        {
            return Problem(title: "Link ticket is invalid or has expired", statusCode: StatusCodes.Status400BadRequest);
        }

        var properties = new AuthenticationProperties();
        properties.Items[ExternalAuth.ModeKey] = ExternalAuth.LinkMode;
        properties.Items[ExternalAuth.LinkUserIdKey] = userId;
        return Challenge(properties, scheme);
    }

    [HttpPost("external/link-ticket")]
    [Authorize]
    [Produces("application/json")]
    public async Task<ActionResult<string>> IssueExternalLoginLinkTicket(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        var ticket = await mediator.Send(new IssueExternalLoginLinkTicketCommand(userId), cancellationToken);
        return Ok(ticket);
    }

    /// <summary>Exchanges the one-time code from the OAuth callback redirect for real tokens.</summary>
    [HttpPost("external/complete")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> CompleteExternalLogin(ExternalLoginCompleteRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CompleteExternalLoginCommand(request.Code), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Maps the URL segment to a scheme name and confirms it's actually registered — Google/
    /// Facebook are only registered when configured (see Program.cs), so an unconfigured
    /// provider must produce a clean 400, not the framework's own "no handler for scheme"
    /// exception (which would otherwise surface as a raw 500).
    /// </summary>
    private async Task<string?> ResolveConfiguredSchemeAsync(string provider)
    {
        var scheme = provider.ToLowerInvariant() switch
        {
            "google" => GoogleDefaults.AuthenticationScheme,
            "facebook" => FacebookDefaults.AuthenticationScheme,
            _ => null,
        };

        if (scheme is null)
        {
            return null;
        }

        return await schemeProvider.GetSchemeAsync(scheme) is not null ? scheme : null;
    }

    // [Produces("application/json")]: without it, ASP.NET Core's default content negotiation
    // writes a bare string ActionResult as text/plain (unquoted), not JSON — silently breaking
    // any JSON-only client like ClientApp/src/api/httpClient.ts's apiFetch, which always calls
    // response.json(). Found while building IssueExternalLoginLinkTicket below (same shape).
    [HttpPost("2fa/enable")]
    [Authorize]
    [Produces("application/json")]
    public async Task<ActionResult<string>> EnableTwoFactor(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        var key = await mediator.Send(new EnableTwoFactorCommand(userId), cancellationToken);
        return Ok(key);
    }

    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableTwoFactor(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        await mediator.Send(new DisableTwoFactorCommand(userId), cancellationToken);
        return NoContent();
    }

    [HttpPost("2fa/recovery-codes")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<string>>> GenerateRecoveryCodes(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        var codes = await mediator.Send(new GenerateRecoveryCodesCommand(userId), cancellationToken);
        return Ok(codes);
    }

    /// <summary>Closes the gap where registration issued a confirmation link with no endpoint to call it against.</summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var confirmed = await mediator.Send(new ConfirmEmailCommand(request.UserId, request.Token), cancellationToken);
        return confirmed ? NoContent() : Problem(title: "Email confirmation failed", statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Re-issues a confirmation link, offered by the sign-in page when a sign-in is refused for an
    /// unconfirmed email. Neutral 202 on the same terms as <see cref="ForgotPassword"/>.
    /// </summary>
    [HttpPost("confirm-email/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-endpoints")]
    public async Task<IActionResult> ResendEmailConfirmation(
        ResendEmailConfirmationRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResendEmailConfirmationCommand(request.Email), cancellationToken);
        return Accepted();
    }

    /// <summary>
    /// Relays a locked-out user's message to the support mailbox. The destination is server-side
    /// configuration and never appears in the contract, so the page can offer "contact an
    /// administrator" without publishing an address.
    /// </summary>
    [HttpPost("account-support")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-endpoints")]
    public async Task<IActionResult> RequestAccountSupport(
        AccountSupportRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new RequestAccountSupportCommand(
                request.Email, request.Message, HttpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);

        return Accepted();
    }

    /// <summary>
    /// Always answers 202, whatever the address turns out to be: the body, the status and — because
    /// the handler enqueues rather than sends — the response time are identical for an account that
    /// exists and one that does not (FR-003, SC-005).
    /// </summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-endpoints")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new RequestPasswordResetCommand(request.Email, HttpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);

        return Accepted();
    }

    /// <summary>
    /// Whether a reset link is still redeemable, so the reset page can show "this link is no
    /// longer valid" on load instead of after the user has typed and confirmed a new password.
    /// Does not consume the token.
    /// </summary>
    [HttpPost("password/reset/validate")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-endpoints")]
    public async Task<IActionResult> ValidateResetToken(
        ValidateResetTokenRequest request, CancellationToken cancellationToken)
    {
        var valid = await mediator.Send(
            new ValidatePasswordResetTokenQuery(request.UserId, request.Token), cancellationToken);

        // POST, not GET: the token would otherwise sit in the query string of a request that
        // proxies and server logs record in full.
        return valid
            ? NoContent()
            : Problem(
                title: "Reset link is no longer valid",
                detail: "This password reset link has expired or has already been used. Request a new one to continue.",
                statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Redeems an emailed reset link. Never returns tokens, so an account with two-factor
    /// enrolment still has to satisfy it at the next sign-in (FR-012).
    /// </summary>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-endpoints")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResetPasswordCommand(request.UserId, request.Token, request.NewPassword),
            cancellationToken);

        return result.Outcome switch
        {
            PasswordResetOutcome.Success => NoContent(),
            PasswordResetOutcome.PasswordPolicyViolation => ValidationProblem(
                new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [.. result.Errors ?? []],
                })
                {
                    Title = "Password does not meet requirements",
                }),

            // One response for every rejection cause — see ResetPasswordCommandHandler (FR-005).
            _ => Problem(
                title: "Reset link is no longer valid",
                detail: "This password reset link has expired or has already been used. Request a new one to continue.",
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("auth-endpoints")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();

        // The caller's own refresh token identifies the session to spare (FR-010). Read from the
        // cookie rather than the body: a client cannot nominate someone else's session that way.
        Request.Cookies.TryGetValue(RefreshTokenCookie.Name, out var actingRefreshToken);

        var result = await mediator.Send(
            new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword, actingRefreshToken),
            cancellationToken);

        return result.Outcome switch
        {
            ChangePasswordOutcome.Success => NoContent(),
            ChangePasswordOutcome.CurrentPasswordRequired => Problem(
                title: "Current password is required", statusCode: StatusCodes.Status400BadRequest),
            ChangePasswordOutcome.SameAsCurrentPassword => Problem(
                title: "New password must differ from the current one", statusCode: StatusCodes.Status400BadRequest),
            ChangePasswordOutcome.PasswordPolicyViolation => ValidationProblem(
                new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [.. result.Errors ?? []],
                })
                {
                    Title = "Password does not meet requirements",
                }),
            _ => Problem(title: "Current password is incorrect", statusCode: StatusCodes.Status400BadRequest),
        };
    }

    /// <summary>
    /// Lets Settings offer "set a password" instead of "change password" for an account created
    /// through an external provider, without having to guess (FR-014).
    /// </summary>
    [HttpGet("password/status")]
    [Authorize]
    public async Task<ActionResult<PasswordStatusResponse>> GetPasswordStatus(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        var hasPassword = await mediator.Send(new GetPasswordStatusQuery(userId), cancellationToken);

        return Ok(new PasswordStatusResponse(hasPassword));
    }

    [HttpPost("change-email/request")]
    [Authorize]
    public async Task<IActionResult> RequestEmailChange(RequestEmailChangeRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        await mediator.Send(new RequestEmailChangeCommand(userId, request.NewEmail), cancellationToken);
        return NoContent();
    }

    [HttpPost("change-email/confirm")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ConfirmEmailChangeCommand(request.UserId, request.NewEmail, request.Token), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("external-logins")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ExternalLoginResponse>>> GetExternalLogins(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        var logins = await mediator.Send(new GetExternalLoginsQuery(userId), cancellationToken);
        return Ok(logins.Select(l => new ExternalLoginResponse(l.Provider, l.ProviderKey, l.DisplayName)));
    }

    [HttpDelete("external-logins/{provider}/{providerKey}")]
    [Authorize]
    public async Task<IActionResult> RemoveExternalLogin(string provider, string providerKey, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstUserId();
        var result = await mediator.Send(new RemoveExternalLoginCommand(userId, provider, providerKey), cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult(IdentityOperationResult result) => result.Status == IdentityResultStatus.Success
        ? NoContent()
        : Problem(title: "Operation failed", detail: string.Join(' ', result.Errors ?? []), statusCode: StatusCodes.Status400BadRequest);

    private ActionResult<AuthResponse> ToActionResult(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.Success => Ok(ToResponse(result)),
        AuthOutcome.RequiresTwoFactor => Ok(new AuthResponse(result.UserId, null, null, RequiresTwoFactor: true)),
        AuthOutcome.EmailNotConfirmed => Problem(title: "Email not confirmed", statusCode: StatusCodes.Status403Forbidden),
        AuthOutcome.LockedOut => Problem(title: "Account locked out", statusCode: StatusCodes.Status423Locked),
        _ => Problem(title: "Invalid credentials", statusCode: StatusCodes.Status401Unauthorized),
    };

    /// <summary>
    /// Single choke point for every successful auth outcome (Register, Login, LoginTwoFactor,
    /// CompleteExternalLogin, and Refresh via <see cref="ToActionResult(AuthResult)"/>) — sets
    /// both httpOnly cookies here once rather than duplicating it per action. The access-token
    /// cookie exists solely so SignalR hub connections can authenticate without a JS-managed
    /// `accessTokenFactory` (see <see cref="AccessTokenCookie"/>) — REST calls keep using the
    /// `Authorization: Bearer` header from the response body, unchanged.
    /// </summary>
    private AuthResponse ToResponse(AuthResult result)
    {
        if (result.RefreshToken is not null)
        {
            Response.Cookies.Append(
                RefreshTokenCookie.Name,
                result.RefreshToken,
                RefreshTokenCookie.BuildOptions(TimeSpan.FromDays(jwtOptions.Value.RefreshTokenLifetimeDays)));
        }

        if (result.AccessToken is not null)
        {
            Response.Cookies.Append(
                AccessTokenCookie.Name,
                result.AccessToken,
                AccessTokenCookie.BuildOptions(TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenLifetimeMinutes)));
        }

        return new(result.UserId, result.AccessToken, result.AccessTokenExpiresAtUtc, RequiresTwoFactor: false);
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete(RefreshTokenCookie.Name, RefreshTokenCookie.DeleteOptions);
        Response.Cookies.Delete(AccessTokenCookie.Name, AccessTokenCookie.DeleteOptions);
    }
}
