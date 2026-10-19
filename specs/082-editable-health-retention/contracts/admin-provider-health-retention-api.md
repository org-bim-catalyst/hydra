# Contract: Admin Provider Health Retention

**Feature**: 082-editable-health-retention | **Base path**: `/api/v1/admin/provider-health/retention`

## Authorization

| Operation | Permission | Notes |
|-----------|------------|-------|
| GET | `admin.operational-failures.view` | Same key the read-only page uses today |
| PUT | `admin.operational-failures.manage` | New write; no new permission key introduced |

Both endpoints are rate-limited by the existing `admin-endpoints` policy. Unauthenticated callers receive 401; callers without the permission receive 403.

## GET `/api/v1/admin/provider-health/retention`

Returns the effective retention window, its origin and the version needed to change it.

**200 OK**

```json
{
  "retentionDays": 30,
  "intervalMinutes": 2,
  "source": "Stored",
  "rowVersion": "AAAAAAAAB9E="
}
```

| Field | Type | Meaning |
|-------|------|---------|
| `retentionDays` | integer | Effective days of health-check history kept |
| `intervalMinutes` | number | Health-check interval (unchanged by this feature; from `ProviderHealthCheck:Interval`) |
| `source` | `"Stored"` \| `"Configuration"` | `Configuration` only when the singleton row is missing |
| `rowVersion` | string \| null | Base64 row version to send as `If-Match`; `null` when `source` is `Configuration` |

The response is unchanged in shape apart from the two added fields; existing consumers of `retentionDays` and `intervalMinutes` are unaffected.

## PUT `/api/v1/admin/provider-health/retention`

Replaces the retention window.

**Headers**

```http
If-Match: AAAAAAAAB9E=
Content-Type: application/json
```

**Body**

```json
{ "retentionDays": 14 }
```

**200 OK** — body is the same representation as GET, with the new value and the new `rowVersion`.

**428 Precondition Required** — `If-Match` missing or not valid base64.

```json
{
  "status": 428,
  "title": "If-Match required",
  "detail": "Send the settings' rowVersion in the If-Match header."
}
```

**409 Conflict** — the row changed after the caller read it. The client must reload. The response includes the machine-readable extension `"reason": "ConcurrencyConflict"`.

**400 Bad Request** — the value is outside 1–365 or missing. This is the platform's standard FluentValidation mapping; the response includes `errors` keyed by property (for example `RetentionDays`).

**403 / 401 / 429** — authorization and rate-limit failures, per the existing problem-details middleware.

## Behavioural rules

1. `retentionDays` is a whole number in `[1, 365]`.
2. A successful PUT writes exactly one audit row (`ProviderHealthRetentionAuditLogs`) in the same transaction, recording the actor, previous value and new value.
3. A rejected PUT (400/409/428) writes no audit row and changes no value.
4. A successful PUT does not delete any health checks immediately; the next daily run uses the new value.
5. When the singleton row is missing, GET reports `source: "Configuration"` and `rowVersion: null`. PUT cannot succeed in that state: without a version it is rejected with 428, and with any supplied version it is rejected with 409. The migration seeds the row, so this is a recovery-only state.
6. The cleanup job reads the stored value and falls back to `ProviderHealthCheck:RetentionDays` (default 30) only when the row is missing.
