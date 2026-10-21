# ADR 0020: Service API Key Authentication for Map Capture

**Status:** Accepted

**Date:** 2026-10-20

**Feature:** [specs/083-viewer-map-capture](../../specs/083-viewer-map-capture/spec.md)

## Context

specs/083 adds a server-to-server map capture endpoint: a trusted service asks for a cropped,
unlabelled satellite PNG of a named place or a coordinate pair and receives the bytes. It is
explicitly **not** a user-facing feature — there is no viewer UI, no download panel, and no
persistence of any kind. The client is a pipeline or an internal service, not a person with a
browser session.

The platform's existing authentication is ASP.NET Identity + JWT bearer tokens aimed at signed-in
users, plus a cookie-backed `/auth/session` route for the browser. None of that fits a machine
caller: there is no user to sign in, no refresh token to rotate, and no UI to host a consent
screen. The endpoint still has to be **authorized** (constitution §8) and **rate limited**, and
it must never spend an unauthenticated caller's budget on a Google imagery fetch.

## Decisions

### A static service API key, not client-credentials JWT

`ServiceApiKeyAuthenticationHandler` reads an `X-Api-Key` header and compares it against the
keys configured under `MapCapture:ApiKeys`. On a match it issues `ClaimTypes.NameIdentifier` =
the configured caller name, `ClaimTypes.Name` = the same, and a `Service` role. Comparison hashes
both sides with SHA-256 and uses `CryptographicOperations.FixedTimeEquals`, so a caller cannot
learn a valid key from response timing, and a length mismatch does not short-circuit the
comparison.

**Alternative rejected**: an OAuth2 client-credentials flow (a service registering as a client and
receiving a signed JWT). Rejected because it needs a client registry, a token endpoint, an issuer,
an audience, key rotation, and revocation — all of which are per-caller state. The capture
endpoint is a *fixed set of trusted services* whose key changes when an administrator rotates it,
not a multi-tenant federation. The cost of the static key is that a leaked key must be rotated
out of band; the cost of client-credentials is building a token subsystem for a handful of
callers.

### The scheme is additive, not a replacement

The handler is registered with `.AddScheme<ServiceApiKeyAuthenticationOptions, ServiceApiKeyAuthenticationHandler>(...)`
on the existing `AddAuthentication(JwtBearerDefaults...)` builder. **JWT remains the default
scheme** for the whole API; the map-capture controller opts in with
`[Authorize(AuthenticationSchemes = ServiceApiKeyAuthenticationOptions.SchemeName)]`. A caller
that presents a valid user JWT to the capture endpoint is rejected, and a caller that presents a
service key to a user endpoint is rejected. The two identities never mix.

### No service key is a request-time 401, not a startup failure

`MapCaptureOptions.ApiKeys` is deliberately **excluded** from the options validation
(`Validate` + `ValidateOnStart` in `AskLucy.Application/DependencyInjection.cs`). Only the
numeric bounds are validated. A deployment with no service key configured starts normally and
logs one startup warning; the endpoint then answers every request with `401`. The alternative —
failing host startup — would take down every other feature of the platform because one
integration is not configured yet.

### Rate limiting partitions on the service identity, not the IP

The `map-capture-endpoints` fixed-window policy uses
`RateLimitPartitions.UserOrClientKey(context)`. Because the handler sets
`ClaimTypes.NameIdentifier`, the partition key is the configured **caller name**, so two services
behind one IP get their own buckets, and one service cannot exhaust another's budget. The permit
limit comes from `MapCapture:RateLimitPerMinute` (default 60).

### The feature is stateless

No entity, no `DbSet`, no migration, no repository, no file written to
`FileStorage:RootPath`, no temp file. The response is composed in memory and streamed. A capture
is reproducible from its inputs, so persisting it would only add a deletion policy and a
lifecycle for no benefit. (FR-014 / SC-004.)

### Overlays are limited to building footprints

`building-footprints` is the only overlay the capture path supports. `site-boundary` is *parsed*
— so the failure message can name the value the caller sent — and then refused with `422
OverlayUnavailable` before the renderer is called.

**Alternative rejected**: rendering the site's boundary polygon over the imagery. Rejected for now
because the boundary the platform knows about is a *chat-scoped, AI-assisted* resolution: it is
resolved per conversation, may be user-edited, and is not a stable property of a coordinate pair.
A server-to-server capture has no conversation, so it has no boundary to draw. The overlay will be
added when a boundary is addressable independently of a conversation.

### Google is the primary renderer; Esri World Imagery is the satellite-only fallback

`MapImageRendererRouter` sends every request to the Google Static Maps renderer when a Google
key is configured, and to the Esri World Imagery renderer when it is not. The Esri path serves
`satellite` only; every other mode with no Google key returns `422 ModeUnavailable`, because
silently substituting a different map type for the one the caller asked for would violate the
no-silent-failure rule.

Both renderers land on the **same 1280×1280 frame** (Google: `size=640x640&scale=2`; Esri: a
`1280,1280` bbox export) so the 14 px per-edge crop and the 1252×1252 output contract hold
regardless of which provider served the request.

## Deviation from constitution §8: no immutable audit trail for service auth events

Constitution §8 requires security-relevant events to be recorded on an **immutable audit trail**.
This endpoint does not do that. Authentication outcomes (accepted, missing, unknown key) are
written to the structured log at `Information`/`Warning` by the handler, and the capture itself
is logged with the caller name, resolved location, mode, overlays, and elapsed time — never the
key.

**This is a pre-existing platform-wide gap, not one introduced here.** The JWT path records
neither an immutable trail nor a structured auth event; the operational failure trail
(ADR 0017) covers AI-provider and operational failures, not authentication. Building an
append-only audit subsystem for one endpoint would be a new platform capability, disproportionate
to the feature, and inconsistent with every other authentication path in the codebase. The
deviation is recorded here so it is visible rather than silent, and so the eventual fix is a
platform change that upgrades *all* auth paths at once rather than a special case bolted onto
map capture.

## Consequences

- Administrators must provision a service key per caller (Settings → configuration, or
  user-secrets locally) before anything can call the endpoint. The key is never stored in a
  tracked file: `appsettings.Production.json.example` carries only an empty `MapCapture:ApiKeys`
  array.
- A leaked key is a config rotation, not a token revocation. The caller name it was issued under
  keeps its rate-limit bucket and its log identity, so rotation is auditable.
- Every capture spends a geocoding call and an imagery fetch against a paid provider, so the
  rate limit is a cost control as much as an abuse control.
- The endpoint is not reachable from the browser UI. Client-side capture from the live viewer
  (persisted captures tied to a conversation and a user, a download panel, knowledge-base
  sharing) is deliberately deferred to a later feature; it would need a different identity model
  and would reintroduce persistence.

## Related

- [specs/083-viewer-map-capture/contracts/map-capture-api.md](../../specs/083-viewer-map-capture/contracts/map-capture-api.md) — the wire contract.
- [specs/083-viewer-map-capture/research.md](../../specs/083-viewer-map-capture/research.md) — the full decision log (D1–D6).
- [specs/083-viewer-map-capture/quickstart.md](../../specs/083-viewer-map-capture/quickstart.md) — the runnable validation guide.
- [ADR 0017: Operational failure trail](0017-operational-failure-trail.md) — the trail that exists today.

---

## Addendum (2026-10-21): the AI Tools admin area

The `map_snapshot` conversation capability (specs/083) is an `IAgentTool`. An administrator had
nowhere to see it: the admin panel showed AI providers, AI capabilities, agent policies, and
system agents, but never the tools an agent can actually call.

### It is read-only, because the catalogue is a fact, not a setting

The area lists `AgentToolCatalog.All` — the native tools plus the currently-active MCP tools —
which is the same merged set the agent runtime dispatches on. Reading the catalogue rather than
re-deriving it from the DI registrations is what guarantees the admin view can never show a tool
the runtime would refuse, or hide one it would use. There is no edit affordance, and none is
planned: what a deployment can call is a fact about the deployment, exactly as the registered
system agents are (which is why that screen is read-only too).

### It reads the same catalogue the runtime does, so MCP tools appear for free

MCP tool names are always namespaced `mcp:{serverId}:{toolName}` and native tool names are plain
identifiers, so the two sets are disjoint by construction and the view can tell them apart by
that prefix. A newly connected MCP server's tools show up on the next page load, with no
additional wiring.

### View-only permission, and the built-in Administrator holds it for free

`admin.ai-tools.view` is a single `View`-level key in `AdminPermissionCatalog`. A new catalogue
key needs no role wiring: `BuiltInRolePermissions.Administrator` derives from `PermissionSet.Full`,
so the built-in Administrator holds every catalogued key except the super-user-controlled one. A
custom role that wants the area is granted the key explicitly, like any other.

### The one deliberate departure from the existing permission matrix test

`PermissionEnforcementMatrixTests` covers the new endpoint too, but its 403-path cases write an
audit row to the shared test database, and every one of them fails intermittently with a
connection timeout against that database — a pre-existing flakiness, not a property of this
endpoint. `AdminAiToolsEndpointTests` therefore asserts the same two cases directly, so this
area's authorization boundary has a test that does not depend on the shared database being
healthy.
