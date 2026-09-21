# Contract: External Login Claim Mapping & Sync

No public HTTP endpoint is added or changed by this feature (`GET /api/v1/users/me`,
`PUT /api/v1/users/me/avatar`, and the existing `/api/v1/auth/external/{provider}/callback`
routes are all unchanged). The contracts affected are internal: the claims each provider must
supply, and the two Application-layer message shapes that carry them.

## Provider Claim Contract

| Claim (as read by `ExternalAuth.HandleTicketReceivedAsync`) | Google source | Facebook source |
|---|---|---|
| `ClaimTypes.NameIdentifier` | `sub` (existing, unchanged) | `id` (existing, unchanged) |
| `ClaimTypes.Email` | `email` (existing, unchanged) | `email` (existing, unchanged) |
| `email_verified` | `email_verified` (existing, unchanged) | n/a (existing, unchanged — presence of `email` implies verified) |
| `ClaimTypes.GivenName` | `given_name` (default-mapped by the framework's Google handler) | `first_name` (requires `options.Fields.Add("first_name")` + explicit `MapJsonKey`) |
| `ClaimTypes.Surname` | `family_name` (default-mapped) | `last_name` (requires `options.Fields.Add("last_name")` + explicit `MapJsonKey`) |
| `urn:google:picture` / `urn:facebook:picture` | `picture` (requires explicit `MapJsonKey`) | `picture.data.url` (requires `options.Fields.Add("picture")` + `MapCustomJson` for the nested path) |

Any claim absent from a given sign-in's ticket is passed through as `null` — never defaulted to
an empty string — so downstream "leave unchanged when missing" (FR-004) logic can distinguish
"not supplied" from "supplied but empty."

## `ProcessExternalLoginCallbackCommand` (extended)

**Direction**: Web → Application (in-process MediatR request)

```csharp
public sealed record ProcessExternalLoginCallbackCommand(
    string Provider,
    string ProviderKey,
    string? Email,
    bool EmailVerified,
    string? LinkToUserId,
    string? FirstName,
    string? LastName,
    string? PictureUrl
) : IRequest<string?>;
```

**Backward compatibility**: existing callers/tests constructing this record positionally will
need updating (it is a `record`, not a versioned wire contract — this is an internal Application
message, not a public API surface, so no version bump applies per constitution §6 scope).

## `IExternalProfilePictureSyncJob` (new)

**Direction**: Application → Infrastructure (Hangfire-dispatched, in-process)

```csharp
public interface IExternalProfilePictureSyncJob
{
    Task SyncAsync(string userId, string pictureUrl, CancellationToken cancellationToken = default);
}
```

**Preconditions**: `userId` refers to an account that was just resolved/created/linked by
`ResolveExternalLoginAsync` in the same request; `pictureUrl` is the raw claim value (untrusted
beyond having originated from the OAuth provider's ticket) — the implementation MUST validate its
host before fetching (research.md Decision 6).

**Postconditions on success**: `ApplicationUser.AvatarFileName` for `userId` points at a newly
stored file containing the fetched image.

**Postconditions on failure** (invalid host, fetch error, oversize, storage error): no change to
`ApplicationUser.AvatarFileName`; a structured warning is logged with `userId` and `provider`
context; the failure is never surfaced to the user (sign-in already completed before this job
runs). A downloaded payload that fails magic-byte validation (`IImageContentValidator`) is
treated identically to a fetch error.
