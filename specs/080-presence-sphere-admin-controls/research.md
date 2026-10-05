# Research: Presence Sphere Admin Controls

## D1 - Where the settings live
- **Decision**: A new single-row table `PresenceSphereSettings`, the same pattern as `DictationEngineSetting` (fixed singleton Id; the row is created on the first save).
- **Rationale**: Proven in this repo and trivially queryable, and it needs no deployment to change. A missing row simply means the defaults, which satisfies FR-018.
- **Alternatives**: `appsettings.json` (needs a deploy and a hand-edited production file, so it fails FR-009); a generic key-value "workspace settings" table (YAGNI with one consumer; revisit at the third real use).

## D2 - Concurrency
- **Decision**: Last write wins, with no row-version check on the update. `ModifiedBy` and `ModifiedAtUtc` from `BaseEntity` are stored and shown.
- **Rationale**: The spec chooses last-write-wins and "last changed by/at" on the page.
- **Alternatives**: 409 on a stale write as in 078. Rejected: a decorative setting does not justify the retry experience.

## D3 - Permissions
- **Decision**: New `AdminArea.Appearance` with `admin.appearance.view` and `admin.appearance.manage`. The catalogue grows from 23 to 25 permissions. The front-end mirror in `adminPermissions.ts` is updated by hand (it is kept in sync by hand today), as are the tests that assert 23.
- **Rationale**: Spec FR-006. Reusing `admin.ai-providers.*` (as 078 did) would blur "change AI vendors" with "change a cosmetic".
- **Alternatives**: Super-User-only. Rejected: the spec says grantable through roles.

## D4 - Read endpoint
- **Decision**: `GET /api/v1/appearance/presence-sphere`, authentication only. It returns the stored values or the defaults, never 404.
- **Rationale**: FR-005 and FR-018. Applying the defaults on the server means client and server cannot disagree about "today's look".
- **Alternatives**: Embed in `/users/me`. Rejected: it couples an unrelated payload and its failure modes.

## D5 - Write endpoint and validation
- **Decision**: `PUT /api/v1/appearance/presence-sphere` with `RequirePermission("admin.appearance.manage")`, replacing all three fields. The ranges are constants on the Domain entity, used by both the FluentValidation validator and the entity's `Update` guard, so they exist once. A 400 Problem Details response names the allowed range (FR-012).
- **Rationale**: Constitution sections 6 and 8; defence in depth with a single source for the limits.
- **Alternatives**: PATCH per field. Rejected: the page saves all three together.

## D6 - Failure handling on the user side
- **Decision**: `usePresenceSphereSettings` is a TanStack Query that supplies the defaults while loading and on error, so the sphere never waits on it (SC-007). A query error is captured through the client's existing error-reporting path and logged; there is no toast.
- **Rationale**: FR-014, and the repo's reading of section 2.VIII: capture and log every failure for diagnosis; hiding a failure the user cannot act on from the end user is not a violation.
- **Alternatives**: Block the card until loaded (slower first paint) or toast the user (nothing they can do about it). Both rejected.
- **To confirm in tasks**: locate the existing client error-capture helper and reuse it rather than adding one.

## D7 - Applying the values in the scene
- **Decision**: Replace the module-level `SPHERE_CARD_FILL`, `PARTICLE_SIZE_SCALE` and the fixed `enableZoom={false}` with props that come from the settings. Fill drives `sphereCameraDistance(fill)`, which is already parameterised. The dot multiplier multiplies the existing `PARTICLE_SIZE_SCALE`, so 1.0x equals today. With zoom on, OrbitControls zoom gets `minDistance` and `maxDistance` derived from the base distance so zoom stays within 1/4x to 2x of normal (FR-004). The live camera must be updated when the fill changes, not only the initial `camera` prop.
- **Rationale**: The scene already parameterises most of this, so it is the smallest change.
- **Alternatives**: Re-mount the canvas on every change. Rejected: it flickers, loses the sphere's state, and the preview must update live.

## D8 - Live preview
- **Decision**: The admin page renders the real presence card and scene, fed by the unsaved form values through props rather than through the settings query. Idle state only, with no audio analyser input. The card's size rule is shared so the preview is the same size as in the chat.
- **Rationale**: FR-008. The preview is the same render path users get, so it cannot drift from them.
- **Alternatives**: A static mock. Rejected: it would drift.

## D9 - Change record
- **Decision**: A structured Serilog event on every update (who, old values, new values), plus `ModifiedBy`/`ModifiedAtUtc` shown on the page.
- **Rationale**: There is no general admin audit trail (`RoleAuditLog` and `AgentAuditLog` are area-specific), and the spec says not to create one.

## D10 - Operational notes
- The migration must be applied by hand to the shared persistence test database before CI, as with other recent migrations.
- WebGL cannot be exercised in jsdom: tests assert the props passed to the scene, and the visual check is in the quickstart.
