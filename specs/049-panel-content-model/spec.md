# Feature Specification: Panel Content Model

**Feature Branch**: `049-panel-content-model`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Replace per-feature panel types with a composable content vocabulary plus declarative viewer actions, and configurable panel chrome."

## Context

specs/028 delivered a floating panel framework and four panel types — chart, table, parameters, summary — each a registered pairing of a data shape with a component that draws it. In practice those four differ only in the shape of their data. Treating each as a registered *type* means every new thing Lucy might want to show costs a component, a validation schema, a client registration and a hand-maintained entry in a list the backend keeps in parallel. It also means Lucy cannot show anything she has not been given a type for, even when the content is nothing more than a heading and a few values.

The word "panel type" is doing two jobs. For most panels the content is **data** — Lucy already knows what she wants to say, and the only question is how it is laid out. For a small minority the content is **code**: it holds continuous state, owns its own drawing surface, or needs values flowing back into it as they change. Only the second kind genuinely needs registering.

This feature separates them. Content becomes a composable vocabulary of blocks Lucy assembles freely, so showing something new costs nothing. Blocks may carry actions that drive the viewer, so content can be acted upon rather than only read. Panels declare their own framing, so a small readout and a wide control strip are not forced into the same box. Registration narrows to the minority case it was always meant for.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lucy Presents Composed Content Without New Code (Priority: P1)

A user asks Lucy something whose answer is better seen than read — where a place is, how figures compare, what a document contains. Lucy assembles the answer from a standard set of content building blocks: a heading, some prose, a list of labelled values, a table, a chart, a figure. A panel opens over the viewer showing exactly that composition. When the user asks about something Lucy has never been asked about before, it still works, because she is composing from the same building blocks rather than reaching for a purpose-built layout that would have to exist first.

**Why this priority**: This is the whole point of the feature. Without it, every new kind of answer costs development work, which is the constraint this feature exists to remove. It also subsumes the four existing types, so it must land before anything else can be simplified.

**Independent Test**: Can be fully tested by asking Lucy questions of several different shapes — a location, a comparison, a breakdown, a summary — and confirming each opens a panel presenting the composed content correctly, with no content-specific code having been written for any of them.

**Acceptance Scenarios**:

1. **Given** a user asks Lucy a question with a visual answer, **When** she composes a response from content blocks, **Then** a panel opens over the viewer rendering those blocks in the order given.
2. **Given** a user asks Lucy about a resolved location, **When** she presents its name and details, **Then** the panel shows them correctly, and no content-specific code, registration or backend change was required to make that possible.
3. **Given** a composition mixing several block kinds, **When** the panel renders, **Then** every block is rendered by the same general presentation, consistent in styling and spacing, and legible in both light and dark themes.
4. **Given** a composition longer than the panel, **When** the user views it, **Then** the content scrolls within the panel and the panel itself does not distort or overflow the viewer.
5. **Given** any of the four previously-supported presentations — a chart, a table, a labelled value list, a prose summary — **When** Lucy presents it under the new model, **Then** the user sees a result at least as good as before.

---

### User Story 2 - User Acts on Panel Content to Drive the Viewer (Priority: P2)

Lucy shows a list of things that exist in the viewer. The user activates one — a row, a link, a button — and the viewer responds: the item is selected and brought into view. The user works between the panel and the viewer without the panel being a dead end.

**Why this priority**: Content that can be acted upon is what makes a panel a working surface rather than a printout, and it is what finally makes the panel-to-viewer association from specs/028 do something. It depends on User Story 1 but delivers distinct value.

**Independent Test**: Can be fully tested by having Lucy present content whose entries carry actions, activating them, and confirming the viewer performs exactly the expected action each time.

**Acceptance Scenarios**:

1. **Given** a panel whose content entries carry actions, **When** the user activates one, **Then** the viewer performs that action and the user can see the result.
2. **Given** an entry carries an action, **When** the user views it, **Then** it is visibly distinguishable as activatable and is reachable and operable by keyboard.
3. **Given** an entry carries no action, **When** the user attempts to activate it, **Then** nothing happens and it was never presented as activatable.
4. **Given** an action refers to something in the viewer that no longer exists, **When** the user activates it, **Then** the user is told the target is no longer available rather than the action silently doing nothing.

---

### User Story 3 - Panels Are Framed to Suit Their Content (Priority: P3)

Different panels need different framing. A composed answer wants a conventional titled box. A compact readout wants to be small and unobtrusive with no title bar at all. The framing fits the content instead of every panel being forced into one shape — and whatever the framing, the user can still move a panel out of the way, shrink it, bring it forward and close it.

**Why this priority**: Needed before the viewer can host the range of panels planned for it, and it prevents the framework from ossifying around one panel shape. The feature still delivers value without it, since the default framing works for composed content.

**Independent Test**: Can be fully tested by opening panels declaring different framing — titled and untitled, resizable and fixed, different default sizes — and confirming each renders as declared while remaining movable, minimisable, closable and focusable.

**Acceptance Scenarios**:

1. **Given** a panel declares a title bar, **When** it opens, **Then** it shows a title bar carrying its title and its close and minimise controls.
2. **Given** a panel declares no title bar, **When** it opens, **Then** no title bar is shown, and the user can still move, minimise, close and focus it through an affordance that is discoverable and keyboard-operable.
3. **Given** a panel declares itself fixed-size, **When** the user attempts to resize it, **Then** no resize affordance is offered and the panel keeps its declared size.
4. **Given** a panel declares a default size, **When** it opens, **Then** it opens at that size, subject to the existing minimum size floor and to remaining within the viewer.
5. **Given** panels of any declared framing, **When** the user works with them, **Then** drag, minimise and restore to the exact prior size and position, close, focus and stacking order, and the saved opacity preference all behave as they did before this feature.

---

### User Story 4 - Unrecognised or Unsafe Content Degrades Visibly (Priority: P3)

Something arrives that the system cannot render or must not act on — a block kind it does not recognise, malformed content, an action it does not permit, or a request for a live panel that nothing has registered. The user sees what happened. Nothing silently disappears, nothing unauthorised runs, and the rest of the panel still works.

**Why this priority**: Required by the platform's no-silent-failure rule, and it is the security boundary for actions. It refines the first three stories rather than delivering value alone, but no part of this feature is complete without it.

**Independent Test**: Can be fully tested by submitting content containing an unknown block kind, malformed block content, a disallowed action and an unregistered live panel type, and confirming each produces a visible, understandable outcome while everything valid around it continues to render and work.

**Acceptance Scenarios**:

1. **Given** content containing a block kind the system does not recognise, **When** the panel renders, **Then** that block shows a visible placeholder explaining it cannot be displayed, and every other block in the panel renders normally.
2. **Given** a block whose content is malformed or incomplete, **When** the panel renders, **Then** that block shows a visible error state, and every other block renders normally.
3. **Given** an entry carrying an action that is not permitted, **When** the panel renders, **Then** the entry is rendered inert and not presented as activatable, the action is never performed, and the refusal is recorded for diagnosis.
4. **Given** an action naming a permitted operation but carrying malformed or invalid arguments, **When** the user activates it, **Then** nothing is performed and the user is shown that the action could not be carried out.
5. **Given** a request to open a live panel of a kind nothing has registered, **When** it is handled, **Then** the user sees a clear, visible indication rather than nothing happening.
6. **Given** content that is entirely empty or contains no renderable blocks, **When** it is handled, **Then** no empty panel is opened and the outcome is visible to the user.

---

### Edge Cases

- What happens when content contains far more blocks than fit comfortably in one panel? It must remain scrollable and usable rather than degrading the viewer's performance or spilling outside the panel.
- What happens when a table block carries a very large number of rows or columns? It must stay readable and scrollable within the panel, and must not make the panel unusable or the viewer unresponsive.
- What happens when text within a block contains markup, control characters, or content that looks like instructions? It must be presented as text and never interpreted as formatting, markup or instruction.
- What happens when an image block references something the user is not entitled to see, or that cannot be loaded? The block must show a visible unavailable state rather than a broken element or an empty space.
- What happens when two entries carry conflicting actions and the user activates them in quick succession? Each must be handled independently and in order, with the viewer ending in the state the last action requested.
- What happens when a user activates an action while the panel is minimised or while the viewer is unavailable? The action must not be performed silently against nothing; the user must see why it did not take effect.
- What happens when content arrives while the maximum number of panels is already open? The existing eviction rule applies unchanged — the least-recently-focused panel closes to make room.
- What happens when the same content is presented twice in quick succession? Each must produce its own independently manageable panel rather than one overwriting the other.

## Requirements *(mandatory)*

### Content Vocabulary

- **FR-001**: The system MUST define a versioned vocabulary of content block kinds from which a panel's content is composed, covering at minimum: a heading, a passage of text, a list of labelled values, a table, a chart, a single prominent figure, an image, and a structural separator.
- **FR-002**: A panel's content MUST be an ordered sequence of blocks, rendered in the order supplied, by a single general presentation rather than by per-composition code.
- **FR-003**: Lucy MUST be able to compose any sequence of blocks from the vocabulary when presenting a result, without being restricted to predefined combinations.
- **FR-004**: Presenting a kind of content the system has not presented before MUST require no new code, no registration, and no change to what the server accepts, provided it is expressible in the vocabulary.
- **FR-005**: Block content MUST be rendered as text and data only; markup, embedded formatting instructions and control characters MUST NOT be interpreted, executed, or rendered as anything other than literal text.
- **FR-006**: Panel content MUST be scrollable within the panel when it exceeds the panel's size, without the panel overflowing the viewer.
- **FR-007**: All blocks MUST render consistently with the platform's existing visual language and MUST be legible in both light and dark themes.
- **FR-008**: The vocabulary MUST be versioned, so that a change to it is a deliberate, identifiable event rather than an incidental addition.

### Declarative Actions

- **FR-009**: A block, or an entry within a block such as a table row or a button, MUST be able to carry an action that invokes an operation on the viewer.
- **FR-010**: The set of permitted actions MUST be a closed allowlist drawn from the viewer's already-published command surface; this feature MUST NOT introduce new viewer commands or change the meaning of existing ones.
- **FR-011**: Every action MUST be validated against the allowlist, and its arguments validated, before it is performed. An action naming an operation outside the allowlist MUST never be performed.
- **FR-012**: Content supplied by Lucy MUST NOT be able to cause an operation outside the allowlist to be performed, regardless of how the action is expressed.
- **FR-013**: An entry carrying a valid action MUST be visibly distinguishable as activatable, keyboard reachable and keyboard operable; an entry carrying no action MUST NOT appear activatable.
- **FR-014**: An entry carrying a rejected action MUST be rendered inert and MUST NOT appear activatable, and the rejection MUST be recorded for diagnosis.
- **FR-015**: When an action is performed but cannot take effect — its target no longer exists, or the viewer cannot carry it out — the user MUST be told, rather than the action appearing to succeed or silently doing nothing.
- **FR-016**: Actions MUST be discrete and complete on activation. Content panels MUST NOT be used to carry continuous state or state that flows back into the panel as it changes.

### Panel Framing

- **FR-017**: A panel MUST declare its framing: whether it has a title bar, whether it is resizable, and its default size.
- **FR-018**: A panel declaring a title bar MUST present its title and its close and minimise controls there.
- **FR-019**: A panel declaring no title bar MUST remain movable, minimisable, closable and focusable through an affordance that is discoverable and keyboard-operable.
- **FR-020**: A panel declaring itself fixed-size MUST offer no resize affordance and MUST retain its declared size.
- **FR-021**: A panel MUST open at its declared default size, subject to the existing minimum size floor and to remaining within the visible viewer area.

### Registration and Server Validation

- **FR-022**: Registration of a panel kind MUST be required only for live panels — those whose content is code rather than data because they hold continuous state, own their own drawing surface, or require values flowing back into them as they change.
- **FR-023**: The server MUST validate presented content against the block vocabulary rather than against a list of feature-specific panel kinds, and the previously hardcoded list of panel kinds MUST be removed.
- **FR-024**: Lucy MUST have two distinct affordances: presenting composed content, which is always available to her; and opening a named live panel, which is available only while that kind is registered.
- **FR-025**: A request to open a live panel of an unregistered kind MUST produce a visible, user-understandable indication rather than failing silently.
- **FR-026**: No live panel is delivered by this feature; the registration mechanism MUST simply narrow to that purpose.

### Preserved Behaviour

- **FR-027**: Every panel behaviour delivered by specs/028 MUST continue to work unchanged: drag; resize where the panel allows it; minimise and restore to the exact prior size and position; close; focus and stacking order; the persisted opacity preference applied to open and subsequently opened panels; cascade placement when no position is supplied; automatic eviction of the least-recently-focused panel past the concurrent cap; per-user isolation; and live delivery of Lucy's panel requests.
- **FR-028**: The existing automated coverage for the panel framework, including its end-to-end coverage, MUST continue to pass in substance.
- **FR-029**: Lucy's written reply MUST refer to the panel rather than restating the content the panel already displays.
- **FR-030**: The system MUST NOT open a panel when there is no renderable content to show; the outcome MUST instead be visible to the user.

### Key Entities

- **Content Block**: One unit of presented content, identified by its kind, carrying the data that kind requires and optionally an action. The vocabulary of kinds is fixed and versioned.
- **Panel Content**: An ordered sequence of content blocks, together with the panel's title — the complete description of what a content panel shows.
- **Action**: A declared request to perform one permitted viewer operation, naming the operation and its arguments. Validated against the allowlist before it is ever performed.
- **Action Allowlist**: The closed set of viewer operations content is permitted to invoke, derived from the viewer's published command surface.
- **Panel Framing**: A panel's declared presentation — title bar or none, resizable or fixed, default size.
- **Live Panel Kind**: A registered panel whose content is code rather than data. None ship in this feature; the registry narrows to this purpose.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Presenting a kind of content the system has never presented before requires zero code changes, zero registrations and zero server changes, provided it is expressible in the vocabulary — verified by presenting at least three previously unsupported kinds of content without modifying the system.
- **SC-002**: The server holds no list of feature-specific panel kinds — verified by inspection, with zero such entries remaining.
- **SC-003**: Every presentation the four previous panel types supported is reproducible under the new model, with no loss of information or legibility, across 100% of the existing cases.
- **SC-004**: Every panel behaviour from specs/028 is preserved — verified by the existing automated coverage passing without substantive change, and by manual exercise finding no user-visible difference.
- **SC-005**: No content supplied by Lucy can cause a viewer operation outside the allowlist to be performed — verified by attempting disallowed and malformed actions, with a 0% execution rate.
- **SC-006**: Every unrenderable block, rejected action and unregistered live panel kind produces a user-visible outcome — zero such cases are observable only in logs.
- **SC-007**: A panel containing an unrenderable or malformed block still renders 100% of its remaining valid blocks.
- **SC-008**: All panel content and controls are operable by keyboard alone and meet the platform's accessibility standard, including panels with no title bar.

## Scope

### In Scope

- The versioned content block vocabulary and the single presentation that renders it.
- Declarative actions on blocks and block entries, the closed allowlist, and their validation.
- Per-panel framing declarations.
- Narrowing panel registration to live panels, and removing the server's hardcoded list of panel kinds.
- Migrating the four existing panel types onto the vocabulary.
- The changes to Lucy's panel affordances that follow from the above.

### Out of Scope

- The viewer extension framework (specs/050).
- Any new viewer command, or any change to the meaning of existing viewer commands (specs/051).
- Model loading and per-element properties, and therefore any content that depends on them (specs/051).
- The solar analysis capability (specs/052).
- Building any live panel. The registration mechanism narrows to that purpose but ships none.
- User-installable or third-party content blocks. The vocabulary is part of the application.
- Updating a content panel after it has opened, or content that changes in place.

## Assumptions

- There are no external consumers of the four existing panel kinds, so they are replaced outright rather than kept working alongside the vocabulary.
- The action allowlist for this feature is drawn from viewer operations that are safe to invoke repeatedly and that only change what is shown or selected — selecting and clearing selection, zooming the view, changing layer visibility, changing the view mode and map style. Operations that add, remove or replace viewer content are deliberately excluded until specs/051 defines them properly, as is a bounding-box framing command that exists on the viewer internally but was never published on its external interface — widening that interface is out of this feature's scope.
- A content panel's content is fixed when it opens. Content that changes after opening is the defining characteristic of a live panel and is therefore out of scope here.
- Images referenced by an image block are served through the platform's existing file-access mechanism with its existing entitlement checks; content cannot reference arbitrary external addresses, which would risk leaking user activity to third parties.
- Text blocks carry plain text. No markup or rich-text syntax is interpreted, which keeps model-generated content from becoming a route to injected markup.
- The existing per-user isolation, concurrent panel cap, eviction rule, opacity preference and cascade placement from specs/028 apply unchanged.
- Panels with no title bar are expected to be small readouts and controls; the movement and control affordance for them is a presentation decision to be settled during design, provided it satisfies FR-019.
- Chart rendering reuses whatever the platform already uses for charts; this feature changes how a chart is requested, not how it is drawn.

## Dependencies

- **specs/028-ai-floating-panels** — supplies the panel framework this feature reshapes: the store, the host, the user controls, the opacity preference, the concurrent cap and eviction, and live delivery of panel requests.
- **specs/027-immersive-viewer-platform** — supplies the published viewer command surface from which the action allowlist is drawn. This feature consumes it and does not change it.
- **specs/045-conversational-agent-runtime** — supplies the mechanism by which Lucy offers and invokes the presenting and opening affordances.

## Downstream

This feature is the first of four. It is self-contained and delivers value alone, but it is sequenced first so that the specs that follow build on a settled panel model:

- **specs/050** — viewer extension framework; extensions will contribute the live panel kinds this feature's registry narrows to.
- **specs/051** — viewer scene and content API; will extend the action allowlist as new viewer commands are defined.
- **specs/052** — solar analysis; the first real consumer of both live panels and composed content.
