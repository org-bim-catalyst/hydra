# Feature Specification: Presence Sphere Admin Controls

**Feature Branch**: `080-presence-sphere-admin-controls`

**Created**: 2026-10-05

**Status**: Draft

**Input**: User description: "Admin controls for the presence sphere (the AI presence card's particle sphere shown in the chat): let an administrator tune, from the Admin panel, (1) the particle (dot) size, (2) whether users can zoom the sphere with the mouse wheel / pinch (enable/disable), and (3) how much of its card the sphere fills (sphere-to-card size). Settings apply to every user's sphere, take effect without a redeploy, default to today's look (75% fill, zoom off, current dot size), are readable by every signed-in user and changeable only by administrators, and the admin page shows a live preview while adjusting, with a reset to defaults."

## Context

The chat shows Lucy's presence card: a small card holding an animated sphere of glowing dots that reacts while Lucy listens and speaks. Its look has been tuned by hand in code several times (the card was halved, the sphere was set to fill 75% of the card, zoom was switched off, the dots were made smaller and scaled with the card). Each change needed a code change and a deployment. This feature moves the three settings that keep being tuned into the Admin panel, so an administrator can adjust them directly and see the result straight away.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tune the sphere's look from the Admin panel (Priority: P1)

An administrator opens a new Appearance page in the Admin panel. It shows the current presence sphere settings next to a live preview of the presence card: the dot size, how much of the card the sphere fills, and whether zoom is allowed. While the administrator moves a control, the preview changes immediately. Nothing changes for anyone else until the administrator saves. After saving, every user's presence sphere uses the new settings.

**Why this priority**: This is the whole point of the feature. Without it, every adjustment still needs a code change and a deployment.

**Independent Test**: Sign in as an administrator, open the Appearance page, move the dot size and fill controls, and watch the preview change. Save, then open the chat as a different, non-admin user and check that their sphere matches the preview.

**Acceptance Scenarios**:

1. **Given** an administrator on the Appearance page, **When** they change the dot size, **Then** the preview's dots resize within a moment, without saving and without a page reload.
2. **Given** an administrator on the Appearance page, **When** they change the sphere-to-card fill, **Then** the preview's sphere grows or shrinks inside the card accordingly.
3. **Given** an administrator who has changed settings but not saved, **When** another user opens the chat, **Then** that user still sees the previously saved look.
4. **Given** an administrator who saves new settings, **When** any signed-in user next opens or reloads the chat, **Then** their presence sphere uses the saved settings.
5. **Given** the settings have never been changed, **When** any user opens the chat, **Then** the sphere looks exactly as it does today: 75% fill, zoom off, today's dot size.

---

### User Story 2 - Allow or prevent zooming the sphere (Priority: P2)

An administrator decides whether users may zoom the presence sphere with the mouse wheel or a pinch gesture. With zoom off, which is today's behaviour, the sphere keeps its size inside the card whatever the user does. With zoom on, users can zoom in and out within sensible limits. Rotating the sphere by dragging stays available either way.

**Why this priority**: Zoom was switched off deliberately because a stray scroll could blow the sphere up or shrink it to a speck inside its small card. The switch lets the administrator reconsider that without a deployment, but the sphere is fully usable without it.

**Independent Test**: Switch zoom on and save. Open the chat as any user and scroll over the sphere: it zooms. Switch zoom off and save. Reload the chat and scroll over the sphere: it does not zoom, and the page scrolls normally if it can.

**Acceptance Scenarios**:

1. **Given** zoom is off, **When** a user scrolls or pinches over the sphere, **Then** the sphere's size does not change.
2. **Given** zoom is on, **When** a user scrolls or pinches over the sphere, **Then** the sphere zooms in or out, but never so far that it overflows the card by more than the card itself or shrinks below a quarter of its normal size.
3. **Given** zoom is on and a user has zoomed, **When** they reload the page, **Then** the sphere returns to its normal size, because zoom is not remembered per user.
4. **Given** zoom is on or off, **When** a user drags across the sphere, **Then** it rotates as it does today.
5. **Given** the preview on the Appearance page, **When** the administrator flips the zoom switch, **Then** the preview's sphere behaves accordingly before saving.

---

### User Story 3 - Return to the default look (Priority: P3)

An administrator who has experimented can put everything back to the default look in one action, instead of remembering the original values.

**Why this priority**: It is a convenience. The defaults could be re-entered by hand, but a single reset avoids mistakes.

**Independent Test**: Change all three settings and save. Choose "Reset to defaults": the controls and the preview return to 75% fill, zoom off and today's dot size. Save, and confirm users see the original look.

**Acceptance Scenarios**:

1. **Given** settings that differ from the defaults, **When** the administrator chooses "Reset to defaults", **Then** the controls and the preview show the default values, and nothing is saved until they save.
2. **Given** unsaved changes on the page, **When** the administrator chooses "Discard changes", **Then** the controls and the preview return to the last saved values.

---

### Edge Cases

- **Settings cannot be loaded for a user** (network failure or server error): the sphere still appears, with the default look. The failure is captured and logged for diagnosis. It is not shown to the end user, because a decorative element falling back to its default look is not something they can act on.
- **Saving fails** (a system error, lost connection, or the administrator lost permission meanwhile): the administrator sees a clear error, their unsaved values stay on the page so they can retry, and nothing changes for users.
- **Out-of-range values** (sent by anything other than the page's controls): the system refuses them with a message naming the allowed range, and nothing is saved.
- **Two administrators save at about the same time**: the later save wins. The page shows who last changed the settings and when, so an administrator can see that someone else has changed them.
- **The administrator leaves the page with unsaved changes**: they are warned before leaving, and the changes are lost if they continue.
- **A user has the chat open when settings are saved**: their sphere picks up the new look on their next page load or reload. Open pages are not updated live.
- **The sphere is hidden or not supported on a device**: the settings have no effect there, and nothing else changes.
- **A user without the view permission opens the Appearance page directly**: they get the standard "not authorised" outcome. Reading the sphere's own settings, which the chat needs, stays open to every signed-in user.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST keep one set of presence sphere settings that applies to every user: the dot size, the sphere-to-card fill, and whether zoom is allowed.
- **FR-002**: Dot size MUST be expressed relative to today's dot size, as a multiplier from 0.25× to 2.0× with a default of 1.0×. 1.0× MUST look identical to today's dots at every card size, and the dots MUST keep scaling with the card as they do today.
- **FR-003**: Sphere-to-card fill MUST be expressed as the percentage of the card's height the sphere occupies, from 40% to 95%, with a default of 75%.
- **FR-004**: Zoom MUST be a single on/off setting with a default of off. When on, zooming MUST be limited so the sphere never grows beyond twice its normal size or shrinks below a quarter of it.
- **FR-005**: Every signed-in user MUST be able to read the current settings, because their presence sphere needs them. No user other than an authorised administrator may change them.
- **FR-006**: Changing the settings MUST require a new "Manage appearance" administrator permission, and viewing the Appearance admin page MUST require a new "View appearance" permission. Both MUST be grantable through the existing role and permission management, like every other admin area.
- **FR-007**: The Admin panel MUST include an Appearance page, listed in the admin navigation for users who hold "View appearance". It shows the three settings with suitable controls: sliders for dot size and fill, each with its current value shown, and a switch for zoom.
- **FR-008**: The Appearance page MUST show a live preview of the presence card, the same size as the card users see in the chat, which reflects every control change immediately and before saving.
- **FR-009**: Changes MUST take effect for users only when an administrator saves them, and MUST NOT require a deployment, a server restart or any other change outside the Admin panel.
- **FR-010**: After a save, every user's presence sphere MUST use the new settings from their next page load or reload onward.
- **FR-011**: The Appearance page MUST offer "Reset to defaults", which puts the controls and the preview back to the default values without saving, and "Discard changes", which puts them back to the last saved values.
- **FR-012**: The system MUST refuse any setting outside its allowed range, with a message naming the range, and save nothing in that case.
- **FR-013**: The system MUST record who last changed the settings and when, show that on the Appearance page, and log each change (who, when, old and new values) as an administrative event.
- **FR-014**: When the settings cannot be loaded, the presence sphere MUST fall back to the default look, and the failure MUST be captured and logged for diagnosis.
- **FR-015**: When a save fails, the administrator MUST see an error explaining that the settings were not saved, and their unsaved values MUST stay on the page.
- **FR-016**: The administrator MUST be warned before leaving the Appearance page with unsaved changes.
- **FR-017**: Users without "View appearance" MUST NOT see the page in the admin navigation, and MUST get the standard "not authorised" outcome if they open it directly. Users with "View appearance" but not "Manage appearance" MUST see the settings and the preview, but the controls are read-only and saving is not possible.
- **FR-018**: Until an administrator saves for the first time, every user MUST see today's look exactly, so releasing this feature changes nothing visible.

### Key Entities

- **Presence sphere settings**: the single, workspace-wide set of values that controls how the presence sphere looks and behaves. It holds the dot size multiplier, the sphere-to-card fill percentage, the zoom on/off setting, who last changed it, and when. If none has ever been saved, the defaults apply.
- **Appearance permissions**: two new administrator permissions, "View appearance" and "Manage appearance". They join the existing permission catalogue and are granted through the existing roles.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can change any of the three settings and see the preview update in under half a second per adjustment.
- **SC-002**: An administrator can go from opening the Appearance page to a saved change in under one minute.
- **SC-003**: After a save, 100% of users who load or reload the chat see the new look, with no deployment or restart involved.
- **SC-004**: With no settings ever saved, the presence sphere is indistinguishable from today's: the same fill, dot size and zoom behaviour in a side-by-side comparison.
- **SC-005**: 100% of attempts to change the settings by users without "Manage appearance", and 100% of out-of-range values, are refused, and none of them change what users see.
- **SC-006**: When the settings cannot be loaded, the presence sphere still appears with the default look on 100% of chat loads.
- **SC-007**: The chat page does not load noticeably slower because of this feature: the presence sphere still appears within the same time as before, to within a tenth of a second.

## Assumptions

- The settings are workspace-wide. There are no per-user or per-role overrides in this feature, and users cannot change the sphere's look themselves.
- New settings reach users on their next page load or reload. Pushing changes live to pages already open is out of scope, because a decorative setting that is changed rarely does not justify a live channel.
- Only the three settings named are in scope. Colours, glow intensity, dot count, rotation speed and the card's own size and position stay as they are, and they could be added to the same page later.
- The dot size range of 0.25× to 2.0× and the fill range of 40% to 95% are wide enough for meaningful tuning while preventing a sphere that is invisible or badly overflows its card.
- "Administrator" means any user granted the new appearance permissions through the existing role management. The existing built-in Super User role receives them as it receives every admin permission.
- The live preview uses the same presence card and sphere that users see. It shows the sphere in its idle state, and it does not need to simulate Lucy speaking.
- The existing role/permission management is reused as it is. There is no general administrative audit trail today, and this feature does not create one: a logged event per change plus "last changed by/at" is enough for a decorative setting.
