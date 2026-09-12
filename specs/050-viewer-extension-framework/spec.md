# Feature Specification: Viewer Extension Framework

**Feature Branch**: `050-viewer-extension-framework`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Package viewer capabilities as independently loadable extensions so the viewer core knows none of them by name."

## Context

The viewer is a fixed surface. Every capability it offers — the floating panel host, the point-of-interest markers, the site boundary overlay, the boundary confidence badge — is wired directly into the one component that renders it, which also performs the location-to-map wiring itself. Adding a capability means editing that shared component, and each edit puts every other capability at risk.

The platform's direction — geolocated model loading, solar analysis, measurement, BIM inspection, tools driven by external integrations — requires the opposite arrangement: a small, stable viewer core that knows nothing about what is layered onto it, and capabilities that plug in, start, stop and fail independently. This is the arrangement Autodesk Platform Services established for its own viewer. This feature adopts the arrangement, not the implementation.

It delivers the framework and moves the four capabilities the viewer currently mounts onto it. It deliberately delivers no new user-facing capability: this is the spec that carries the regression risk, and it is cheapest now, while only four things need moving.

## Clarifications

### Session 2026-09-12

- Q: Does the viewer toolbar belong in this feature or a later one? → A: The contract states that an extension may contribute a toolbar entry, and this feature implements a minimal host for it inside the viewer. It is deliberately not designed in detail, because none of the four migrated capabilities has a button; the first real consumer is the solar analysis capability in specs/052, which will drive its design. This avoids both building an elaborate surface with no consumer and changing the contract one feature after writing it.
- Q: How many capabilities migrate here? → A: Exactly the four the viewer surface mounts. The site boundary overlay is sequenced last, so it moves only once the contract is proven by three easier migrations. The weather widget, marker style selector and rotation toggle are mounted by the chat page rather than the viewer, and moving those is a question about where viewer controls live — deferred.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Viewer Capabilities Load as Independent Extensions (Priority: P1)

A user opens the workspace and everything works exactly as before — the map renders, panels appear over it, markers and the site boundary show where they always did. Nothing about the experience has changed. What changed underneath is that each capability is now a separately loadable unit the viewer starts, rather than a fixed part of the viewer itself.

**Why this priority**: Every other story depends on the loading mechanism existing, and this is the only story carrying regression risk for capabilities users already rely on. It must be proven first and proven completely.

**Independent Test**: Can be fully tested by opening the viewer and exercising each migrated capability — panels (drag, resize, minimise and restore, close, focus and stacking, opacity preference, cascade placement, eviction past the cap, error and fallback panels), point-of-interest markers, the site boundary overlay and its confidence badge — and confirming behaviour is indistinguishable from the current release, while the viewer core contains no capability-specific wiring.

**Acceptance Scenarios**:

1. **Given** the viewer opens, **When** its declared set of extensions starts, **Then** every capability in that set becomes active and the viewer presents the same experience as before this feature.
2. **Given** the panel capability is active, **When** a panel is requested, **Then** it behaves exactly as it did before — draggable, resizable where its framing allows, minimisable and restorable to the exact prior size and position, closable, brought to front on interaction, honouring the saved opacity preference, cascade-placed when no position is given, and evicting the least-recently-focused panel past the concurrent cap.
3. **Given** the marker, boundary and badge capabilities are active, **When** a location and its site boundary resolve, **Then** the marker, the boundary highlight and the confidence badge appear exactly as they did before this feature.
4. **Given** an extension is stopped, **When** the user continues using the viewer, **Then** only that capability's contribution disappears; the viewer and every other active extension keep working.
5. **Given** the viewer core, **When** a reviewer inspects what it references, **Then** it references the extension mechanism and no individual capability.

---

### User Story 2 - A New Capability Ships Without Touching the Core (Priority: P2)

Someone adds a new viewer capability. Building it means writing the capability and adding it to the declared set. It does not mean opening the viewer core, the panel framework, or any existing capability.

**Why this priority**: This is the reason the framework exists. It comes after User Story 1 because it is verified by adding a capability rather than by end-user behaviour, and the framework is already valuable once the migration lands.

**Independent Test**: Can be fully tested by adding a capability that contributes an overlay, a live panel kind and a toolbar entry, declaring it, and confirming its contributions appear and work with no edit to the viewer core, the panel framework, or any other capability.

**Acceptance Scenarios**:

1. **Given** a new capability contributing an overlay, a live panel kind and a toolbar entry, **When** it is declared and the viewer opens, **Then** its contributions appear with no change to the viewer core or to another capability.
2. **Given** a capability contributes a live panel kind, **When** a panel of that kind is requested, **Then** it renders through that capability's own presentation, and the panel framework required no change to accommodate it.
3. **Given** a capability is removed from the declared set, **When** the viewer opens, **Then** its contributions are absent and nothing else is affected.
4. **Given** a capability contributes a toolbar entry, **When** the viewer opens, **Then** the entry appears in the viewer's own toolbar, distinct from the application ribbon.

---

### User Story 3 - Capability Failures Are Visible and Contained (Priority: P2)

A capability fails to start — something it depends on is unavailable, its configuration is missing, something in it goes wrong. The user is told which capability is unavailable. The viewer still opens, and every other capability still works.

**Why this priority**: Required by the platform's no-silent-failure rule, and the isolation guarantee is what makes adding capabilities safe rather than risky. It is not optional, but it refines Stories 1 and 2 rather than delivering value alone.

**Independent Test**: Can be fully tested by forcing one capability to fail on start, and separately on stop, and confirming a visible message names the affected capability while the viewer and all other capabilities remain fully usable.

**Acceptance Scenarios**:

1. **Given** an extension fails while starting, **When** the viewer finishes opening, **Then** the user sees a visible message naming the unavailable capability, and the failure is recorded for diagnosis.
2. **Given** an extension fails while starting, **When** the user uses the viewer, **Then** the viewer and every other extension work normally.
3. **Given** an extension fails while stopping, **When** the viewer continues, **Then** the failure is surfaced and does not prevent the remaining extensions stopping or the viewer continuing.
4. **Given** a declared extension that was never registered, **When** the viewer opens, **Then** the unknown capability is surfaced visibly and the remaining extensions still start.
5. **Given** two extensions registered under the same identity, **When** registration happens, **Then** the conflict is reported as a configuration error rather than one silently replacing the other.
6. **Given** an extension that takes an unusually long time to start, **When** the viewer opens, **Then** the viewer is usable while it starts and does not wait on it indefinitely.

---

### User Story 4 - A Toggleable Capability Turns On and Off (Priority: P3)

A capability is one the user switches on and off rather than one that is simply present — a tool, an analysis mode. Turning it off stops it doing anything without unloading it, and turning it back on resumes it.

**Why this priority**: The started/stopped and active/inactive distinction must exist in the contract from the start, because retrofitting a second state axis later would change the contract for every extension already written. No migrated capability needs it yet, so it is verified rather than demonstrated.

**Independent Test**: Can be fully tested with a capability declaring itself toggleable, by activating and deactivating it and confirming its effect appears and disappears while it remains started throughout.

**Acceptance Scenarios**:

1. **Given** a started extension that declares itself toggleable, **When** it is activated, **Then** its effect becomes present and it reports itself active.
2. **Given** an active toggleable extension, **When** it is deactivated, **Then** its effect stops while the extension remains started, and it reports itself inactive.
3. **Given** an extension that does not declare itself toggleable, **When** activation is attempted, **Then** the attempt is rejected visibly rather than silently doing nothing.

---

### Edge Cases

- What happens when an extension is started twice, or stopped having never started? Both must be no-ops that produce no duplicate contributions and no error.
- What happens when an extension's contributions are still on screen as it stops? Its overlays, panel kinds and toolbar entries must be removed and its subscriptions released, leaving nothing orphaned.
- What happens when an extension that contributed a live panel kind stops while a panel of that kind is open? The open panel must be closed or shown as unavailable rather than left rendering against a capability that no longer exists.
- What happens when an extension contributes to a host that does not exist yet — a toolbar entry before the toolbar is created? The contribution must be applied once that host becomes available, not lost.
- What happens when an extension throws while handling a viewer event? The failure must be contained to that extension and surfaced, without breaking the viewer's event delivery to others.
- What happens when two extensions contribute toolbar entries? Both must appear, in a defined and stable order.
- What happens when an extension is stopped while it is still starting? The stop must be honoured and must leave no contributions behind.
- What happens when the viewer itself closes with extensions running? All must be stopped and all contributions withdrawn.
- What happens when an extension attempts something the viewer cannot currently do — issuing a command while no content is loaded? The existing command surface's failure behaviour applies unchanged; this feature must not change it.

## Requirements *(mandatory)*

### Extension Contract

- **FR-001**: The system MUST define a contract that a viewer capability implements in order to be loaded as an extension, comprising a unique stable identity, a start step and a stop step.
- **FR-002**: The contract MUST support an active/inactive state, distinct from started/stopped, for extensions that declare themselves toggleable.
- **FR-003**: An extension MUST declare metadata describing it: a display name, a description, whether it is user-toggleable, and whether it starts with the viewer.
- **FR-004**: Attempting to activate an extension that does not declare itself toggleable MUST be rejected visibly rather than silently ignored.
- **FR-005**: The contract MUST be designed to accept additional contribution kinds and additional context capabilities without existing extensions changing, because later features extend it.

### Registry and Loader

- **FR-006**: The system MUST provide a registry in which an extension is registered once under its identity, and a loader that starts an extension by identity.
- **FR-007**: Registering two extensions under the same identity MUST be reported as a configuration error, never resolved by silently replacing one with the other.
- **FR-008**: Starting an already-started extension, or stopping one that is not started, MUST have no effect and MUST produce neither duplicate contributions nor an error.
- **FR-009**: A declared extension that is not present in the registry MUST be surfaced visibly, and MUST NOT prevent the remaining declared extensions from starting.
- **FR-010**: Stopping an extension that is still starting MUST be honoured and MUST leave no contributions behind.

### Extension Context and Contributions

- **FR-011**: A starting extension MUST receive a context that is its only route to the viewer, supplying the viewer's already-published command and event surface plus helpers for contributing user interface.
- **FR-012**: This feature MUST NOT change the meaning or behaviour of any existing viewer command or event.
- **FR-013**: An extension MUST be able to contribute overlays drawn over the viewer surface, live panel kinds, and viewer toolbar entries, without the viewer core knowing what those contributions are.
- **FR-014**: Every contribution made through the context MUST be tracked by the framework, so that stopping an extension withdraws all of it — overlays, live panel kinds, toolbar entries and event subscriptions — without relying on the extension author to remember.
- **FR-015**: Stopping an extension MUST leave no contribution, subscription or reserved identity behind.
- **FR-016**: Starting or stopping one extension MUST NOT change the behaviour of the viewer core or of any other extension.
- **FR-017**: An extension that fails while handling a viewer event MUST have that failure contained and surfaced, without disrupting event delivery to other extensions.

### Readiness Ordering

- **FR-018**: An extension MUST be able to start before a host it wishes to contribute to exists.
- **FR-019**: The system MUST notify an extension when a contribution host becomes available, and MUST notify it immediately if that host already exists at the time it starts.
- **FR-020**: A contribution made before its host exists MUST be applied once the host becomes available, never discarded.

### Viewer Toolbar

- **FR-021**: The viewer MUST provide a toolbar hosted within the viewer itself, distinct from the application ribbon, into which extensions contribute entries.
- **FR-022**: Entries contributed by more than one extension MUST all appear, in a defined and stable order.
- **FR-023**: A toolbar entry MUST be removed when its contributing extension stops, and the toolbar MUST be absent or empty rather than broken when no entries exist.
- **FR-024**: The toolbar and its entries MUST meet the platform's accessibility standard, including keyboard operability and visible focus.

### Host and Lifecycle

- **FR-025**: The viewer MUST start a declared set of extensions when it opens and stop them when it closes, without the viewer core containing logic specific to any individual extension.
- **FR-026**: The viewer core MUST NOT reference any individual capability after this feature.
- **FR-027**: The viewer MUST remain usable while extensions are starting, and MUST NOT wait indefinitely on an extension that does not finish starting.
- **FR-028**: When the viewer closes, every running extension MUST be stopped and every contribution withdrawn.

### Failure Isolation

- **FR-029**: An extension that fails to start MUST produce a visible, user-understandable indication naming the affected capability, MUST be recorded for diagnosis, and MUST NOT prevent the remaining extensions starting.
- **FR-030**: An extension that fails to stop MUST be surfaced and recorded, and MUST NOT prevent the remaining extensions stopping or the viewer continuing.
- **FR-031**: No extension lifecycle failure may be observable only in logs; every one MUST reach the user.

### Migration

- **FR-032**: The four capabilities the viewer surface currently mounts MUST be migrated to extensions: the floating panel host, the point-of-interest marker overlay, the boundary confidence badge, and the site boundary overlay.
- **FR-033**: The site boundary overlay MUST be migrated last, after the other three, so that the contract is proven before the capability with the most post-release history is moved.
- **FR-034**: Every migrated capability MUST behave identically to its behaviour before this feature, with no user-visible difference.
- **FR-035**: The existing automated coverage for the migrated capabilities, including end-to-end coverage, MUST continue to pass in substance.
- **FR-036**: An extension that contributes a live panel kind MUST have that kind withdrawn when it stops, and any open panel of that kind MUST be closed or shown as unavailable rather than left rendering against a capability that no longer exists.

### Key Entities

- **Viewer Extension**: A self-contained viewer capability with a unique stable identity, which the viewer starts and stops. It knows the viewer; the viewer does not know it.
- **Extension Manifest**: What an extension declares about itself — display name, description, whether it is toggleable, whether it starts with the viewer.
- **Extension Registry**: The catalogue of extensions known to the system, keyed by identity, in which an identity appears at most once.
- **Declared Extension Set**: The list of identities the viewer starts when it opens. Changing what the viewer does means changing this list, not the viewer.
- **Extension Context**: What a starting extension receives — the viewer's published commands and events, plus contribution helpers. An extension's only route to the viewer.
- **Contribution**: Something an extension adds — an overlay, a live panel kind, a toolbar entry, an event subscription — tracked so it can be withdrawn in full when the extension stops.
- **Lifecycle State**: Where an extension is: not started, starting, started, failed, stopped; and for toggleable extensions, active or inactive.
- **Viewer Toolbar**: The control surface hosted inside the viewer into which extensions contribute entries. Distinct from the application ribbon.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every capability the viewer offered before this feature behaves identically after it — verified by the existing automated coverage passing without substantive change, and by manual exercise of each capability finding no user-visible difference.
- **SC-002**: The viewer core references no individual viewer capability — verified by inspection, with zero direct references remaining.
- **SC-003**: Adding a viewer capability that contributes an overlay, a live panel kind and a toolbar entry requires changes to no file outside that capability other than the single declaration that adds it to the set.
- **SC-004**: Stopping any extension leaves zero contributions, subscriptions or reserved identities behind — verified by starting and stopping each extension repeatedly and observing no accumulation.
- **SC-005**: When any single extension fails to start, the viewer still opens and 100% of the remaining extensions remain fully usable.
- **SC-006**: Every extension lifecycle failure produces a user-visible indication naming the affected capability — zero failures are observable only in logs.
- **SC-007**: The viewer becomes usable no later after this feature than before it, measured from opening the workspace to the map being interactive.
- **SC-008**: A contribution made before its host exists is applied in 100% of cases once that host becomes available, and is never lost.

## Scope

### In Scope

- The extension contract, manifest, registry, loader, lifecycle and contribution tracking.
- The extension context, as the sole route from an extension to the viewer.
- Overlay, live panel kind and toolbar entry contributions, and the readiness notification that makes contributing to a not-yet-existing host safe.
- A minimal viewer-hosted toolbar, sufficient to carry contributed entries.
- Converting the viewer surface into a host that starts a declared set.
- Migrating the four capabilities the viewer surface mounts, boundary overlay last.

### Out of Scope

- The weather widget, marker style selector and rotation toggle. These are mounted by the chat page, not the viewer surface. Moving them is a question about where viewer controls belong, deferred to specs/051 or a follow-up.
- Any new viewer command, or any change to the meaning of existing viewer commands (specs/051).
- Scene access, renderer access, render scheduling, model loading, per-element properties, and tracking of drawing resources (specs/051). The contribution ledger must be designed to extend there, but this feature does not draw.
- The solar analysis capability (specs/052).
- Building any live panel. Extensions may contribute the kind; none ships here.
- User-installable, third-party or remotely loaded extensions. Extensions are part of the application.
- Any user-facing interface for browsing, enabling or disabling extensions.
- Per-user or per-tenant extension configuration, and cross-user or shared extension state.
- Elaborating the viewer toolbar's visual design beyond what is needed to carry an entry. Its first real consumer is specs/052.

## Assumptions

- The declared set of extensions is fixed in the application rather than configured per user or per tenant, and there is no extension manager interface.
- Extensions are part of the application rather than fetched at runtime, so an extension failing to start is a defect or an environment problem, not an expected condition.
- Extension state lives within the user's own session, consistent with the per-user isolation the platform already applies to panels and conversations.
- The viewer's existing command and event surface is sufficient for the four capabilities migrated here. If a migrated capability turns out to need something the surface does not expose, that gap is surfaced during planning and resolved in specs/051 rather than by widening the surface quietly.
- Extension lifecycle failures are reported through the platform's existing user-visible error surfaces; no new notification mechanism is introduced.
- The location-to-map wiring the viewer surface performs today is viewer-core behaviour and stays in the host for now. Making content loading itself driveable is specs/051.
- Starting extensions is expected to be fast enough that no progress indication is needed; the requirement is only that the viewer stays usable and does not block indefinitely.

## Dependencies

- **specs/027-immersive-viewer-platform** — supplies the published viewer command and event surface that the extension context hands to extensions. Consumed unchanged.
- **specs/049-panel-content-model** — narrows the panel registry to live panel kinds, which is the contribution kind extensions register into here. 049 must land first.
- **specs/028-ai-floating-panels** — supplies the panel capability being migrated.
- **specs/038-viewer-poi-zoom** — supplies the point-of-interest marker capability being migrated.
- **specs/042-site-boundary-resolution** — supplies the site boundary overlay and confidence badge being migrated.

## Downstream

- **specs/051** — viewer scene and content API; extends the extension context with scene access, render scheduling and content commands, and decides where the chat page's viewer controls belong.
- **specs/052** — solar analysis; the first capability built entirely on this framework, and the first real consumer of the viewer toolbar.
