# Contract: Weather API

Satisfies spec FR-009–FR-011, FR-012a (User Story 4). Follows this repo's REST conventions
(constitution §6): versioned path, `[Authorize]` by default, RFC 9457 Problem Details on error, rate
limited.

## `GET /api/v1/weather/current`

**Auth**: Required (existing JWT bearer).

**Rate limit policy**: `weather-endpoints` (new — `Program.cs`), fixed window, e.g. 30 requests/minute
per authenticated user (mirrors the existing per-feature policy pattern; generous enough for the
widget's periodic refresh plus manual reloads/multiple tabs, tight enough that it can't be used to
proxy-scrape the upstream provider).

### Query parameters

| Name | Type | Required | Validation |
|---|---|---|---|
| `latitude` | `double` | yes | -90 to 90 |
| `longitude` | `double` | yes | -180 to 180 |

### 200 OK

```json
{
  "locationName": "London, United Kingdom",
  "temperatureCelsius": 15.0,
  "condition": "Cloudy",
  "isDaytime": true,
  "observedAtUtc": "2026-08-17T09:00:00Z"
}
```

`condition` is one of: `Clear`, `PartlyCloudy`, `Cloudy`, `Fog`, `Rain`, `Snow`, `Thunderstorm`,
`Windy` (research.md Decision 7) — a closed set, not the upstream provider's raw codes.

### Error responses (RFC 9457 Problem Details, `application/problem+json`)

| Status | `type` suffix | When |
|---|---|---|
| 400 | `validation-failed` | `latitude`/`longitude` missing or out of range (existing `ValidationException` → Problem Details mapping, no new arm needed) |
| 429 | (rate limiter's built-in response) | `weather-endpoints` policy exceeded |
| 502 | `weather-provider-unavailable` | Upstream weather provider errored, timed out, or returned an unparseable response (new `WeatherProviderUnavailableException` → new `ProblemDetailsMiddleware` arm, mirroring the existing `AiProviderUnavailableException` → 502 pattern) |

**Frontend handling**: Per FR-011/spec Edge Cases, the frontend treats *any* non-200 response from this
endpoint (including 502/429) the same way as "provider unavailable" — show a last-known stale reading
with a staleness indicator if one is cached client-side, otherwise hide the widget entirely. This is a
deliberate UI-level suppression of an otherwise-visible error (see plan.md's Constitution Check
"No Silent Failures — documented carve-out"): the failure is still logged (`console.error`, matching
the `SceneBackground` precedent) for telemetry, just not toasted, because the widget is ambient/
supplementary content, not a user-initiated action.

## No persistence (FR-012b)

This endpoint is a pure pass-through lookup — nothing about the request (coordinates, resolved
location, temperature) is written to the database or associated with the calling user's stored
profile. `WeatherProvider` (Infrastructure) calls the upstream provider fresh (or from its own
in-memory/short-TTL cache, an implementation detail) on every request; there is no
`GetCurrentWeatherQueryHandler` write path at all.
