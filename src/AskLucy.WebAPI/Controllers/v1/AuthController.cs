using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.ExternalLogin;
using AskLucy.Application.Authentication.Commands.Login;
using AskLucy.Application.Authentication.Commands.LoginTwoFactor;
using AskLucy.Application.Authentication.Commands.Logout;
using AskLucy.Application.Authentication.Commands.Refresh;
using AskLucy.Application.Authentication.Commands.Register;
using AskLucy.Application.Authentication.Commands.TwoFactor;
using AskLucy.WebAPI.Auth;
using AskLucy.WebAPI.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AskLucy.WebAPI.Controllers.v1;

/// <summary>Preserves FR-009/FR-010/FR-011 through a JWT-based API, per contracts/api-v1.md.</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(ISender mediator) : ControllerBase
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
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RefreshCommand(request.RefreshToken), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return NoContent();
    }

    [HttpPost("external")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> ExternalLogin(ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExternalLoginCommand(request.Provider, request.ProviderKey, request.Email), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("2fa/enable")]
    [Authorize]
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

    private ActionResult<AuthResponse> ToActionResult(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.Success => Ok(ToResponse(result)),
        AuthOutcome.RequiresTwoFactor => Ok(new AuthResponse(result.UserId, null, null, null, RequiresTwoFactor: true)),
        AuthOutcome.EmailNotConfirmed => Problem(title: "Email not confirmed", statusCode: StatusCodes.Status403Forbidden),
        AuthOutcome.LockedOut => Problem(title: "Account locked out", statusCode: StatusCodes.Status423Locked),
        _ => Problem(title: "Invalid credentials", statusCode: StatusCodes.Status401Unauthorized),
    };

    private static AuthResponse ToResponse(AuthResult result) =>
        new(result.UserId, result.AccessToken, result.AccessTokenExpiresAtUtc, result.RefreshToken, RequiresTwoFactor: false);
}
