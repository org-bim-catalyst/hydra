using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Application.Users.Commands.DeleteMyAccount;
using AskLucy.Application.Users.Commands.UpdateMyProfile;
using AskLucy.Application.Users.Commands.UpdateUser;
using AskLucy.Application.Users.Commands.UploadAvatar;
using AskLucy.Application.Users.Queries.GetAllUsers;
using AskLucy.Application.Users.Queries.GetMyProfile;
using AskLucy.WebAPI.Auth;
using AskLucy.WebAPI.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AskLucy.WebAPI.Controllers.v1;

/// <summary>FR-025: avatar moves from an inline DB BLOB to file storage + a signed URL.</summary>
[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController(
    ISender mediator,
    ISignedUrlService signedUrlService,
    IUserProfileRepository profiles,
    IFileStorage fileStorage) : ControllerBase
{
    private static readonly TimeSpan AvatarUrlLifetime = TimeSpan.FromMinutes(15);

    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMe(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMyProfileQuery(), cancellationToken));

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateMyProfileCommand(request.FirstName, request.LastName), cancellationToken);
        return NoContent();
    }

    [HttpPut("me/avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<AvatarUploadResponse>> UploadAvatar(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        await mediator.Send(new UploadAvatarCommand(stream, file.FileName), cancellationToken);

        var userId = User.FindFirstUserId();
        var (expires, signature) = signedUrlService.Sign(userId, AvatarUrlLifetime);
        var url = Url.Action(nameof(GetAvatar), new { userId, exp = expires, sig = signature })!;

        return Ok(new AvatarUploadResponse(url));
    }

    [HttpGet("{userId}/avatar")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvatar(string userId, [FromQuery] string exp, [FromQuery] string sig, CancellationToken cancellationToken)
    {
        if (!signedUrlService.IsValid(userId, exp, sig))
        {
            return Forbid();
        }

        var profile = await profiles.GetByIdAsync(userId, cancellationToken);
        if (profile?.AvatarFileName is null)
        {
            return NotFound();
        }

        var stream = await fileStorage.OpenReadAsync(profile.AvatarFileName, cancellationToken);
        return File(stream, "application/octet-stream");
    }

    /// <summary>Legacy DownloadPersonalData.cshtml equivalent — a self-service JSON export of the caller's own profile fields.</summary>
    [HttpGet("me/personal-data")]
    public async Task<IActionResult> DownloadMyPersonalData(CancellationToken cancellationToken)
    {
        var profile = await mediator.Send(new GetMyProfileQuery(), cancellationToken);
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", "ask-lucy-personal-data.json");
    }

    /// <summary>Legacy DeletePersonalData.cshtml equivalent — irreversible, requires password re-confirmation.</summary>
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeleteMyAccountCommand(request.Password), cancellationToken);
        return result.Status == IdentityResultStatus.Success
            ? NoContent()
            : Problem(title: "Account deletion failed", detail: string.Join(' ', result.Errors ?? []), statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Admin listing — DTO-projected (FR-019), never the raw <c>ApplicationUser</c>
    /// entity the legacy endpoint returned. Role-gated to Administrator/Super User
    /// server-side (FR-017, User Story 4) — replacing the legacy UI-only check.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "AdministratorOrSuperUser")]
    public async Task<ActionResult<IReadOnlyList<UserAdminDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAllUsersQuery(), cancellationToken));

    /// <summary>
    /// Admin update via an explicit, validated command — the request body can carry
    /// only <see cref="UpdateUserRequest"/>'s two fields; nothing else the client sends
    /// (id, passwordHash, roles, ...) is ever read (FR-017/FR-019, closes the legacy
    /// overposting/mass-assignment vulnerability). Role-gated (FR-017, User Story 4).
    /// </summary>
    [HttpPatch("{userId}")]
    [Authorize(Policy = "AdministratorOrSuperUser")]
    public async Task<IActionResult> UpdateUser(string userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateUserCommand(userId, request.FirstName, request.LastName), cancellationToken);
        return NoContent();
    }
}
