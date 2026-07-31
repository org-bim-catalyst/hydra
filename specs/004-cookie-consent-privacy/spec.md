# Feature Specification: Cookie Consent & Privacy Management

**Feature Branch**: `004-cookie-consent-privacy`

**Created**: 2026-07-30

**Status**: Draft

**Input**: User description: "Add cookie consent and privacy page, and add page for cookies in the user's settings page. The cookies consent banner should appear in the main page after the first user login for the first time."

## Clarifications

### Session 2026-07-30

- Q: What consent/compliance posture should the cookie system implement as its default legal model? → A: Strict opt-in globally — non-essential cookies never fire until the user explicitly accepts, applied to every user regardless of location.
- Q: Should the Privacy Page and consent banner text be localized at initial launch? → A: English only at launch; content is centralized so translation is mechanical later, but full translation is not required for v1.
- Q: Before the user makes an explicit consent choice, should the rest of the app be usable? → A: Blocking modal — the user must Accept All, Reject Non-Essential, or Customize+Save before they can interact with anything else in the app.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - First-Login Cookie Consent Banner (Priority: P1)

A user logs into Ask Lucy for the first time (or has never recorded a cookie consent decision). Immediately upon landing on the main app page, they see a consent banner explaining that the platform uses cookies, with clear choices: Accept All, Reject Non-Essential, or Customize. They make a choice and the banner disappears and does not reappear on future logins unless they later change their mind or the cookie policy changes.

**Why this priority**: This is the legally and contractually load-bearing piece — without a working, accurate consent mechanism, the platform cannot lawfully process non-essential cookies for any user. It is also the only piece that must work correctly on day one for every user, so it is the MVP.

**Independent Test**: Can be fully tested by logging in with a fresh account that has no prior consent recorded, confirming the banner appears on the main page, and confirming that choosing "Accept All," "Reject Non-Essential," or a custom combination is persisted and the banner does not reappear on the next login.

**Acceptance Scenarios**:

1. **Given** a user has never recorded a cookie consent decision, **When** they log in and land on the main app page, **Then** a consent banner is displayed offering Accept All, Reject Non-Essential, and Customize options.
2. **Given** the consent banner is displayed, **When** the user selects "Accept All," **Then** all cookie categories (including Functional, Analytics, and Marketing) are marked as accepted, the banner closes, and the decision is saved to their account.
3. **Given** the consent banner is displayed, **When** the user selects "Reject Non-Essential," **Then** only the Essential category remains active, the banner closes, and the decision is saved to their account.
4. **Given** the consent banner is displayed, **When** the user selects "Customize," **Then** they can toggle each non-essential category individually (Essential remains locked on) and save their specific combination.
5. **Given** a user has already recorded a consent decision under the current cookie policy, **When** they log in again, **Then** the banner does not appear.
6. **Given** the consent banner is displayed, **When** the user views it, **Then** it contains a visible link to the Privacy Page for full details.
7. **Given** the consent banner is displayed, **When** the user attempts to interact with any other part of the app (navigation, chat, settings) without making a choice, **Then** that interaction is blocked and the banner remains until the user selects Accept All, Reject Non-Essential, or Customize+Save.
8. **Given** the consent banner is displayed, **When** any user reaches it regardless of their location, **Then** no Functional, Analytics, or Marketing cookies are active until the user records an explicit decision.

---

### User Story 2 - Manage Cookie Preferences from Settings (Priority: P2)

A user who has already made an initial consent choice wants to revisit or change it later. From their account Settings, they open a dedicated Cookie Preferences section, see their current selection per category and when it was last updated, adjust the toggles, and save — with the new preferences taking effect immediately.

**Why this priority**: Users and regulations require an ongoing, easy way to withdraw or change consent — this is the mechanism for that, but it depends on the categories and persistence model established in User Story 1.

**Independent Test**: Can be fully tested by logging in as a user with an existing consent decision, navigating to Settings > Cookie Preferences, changing a toggle, saving, and confirming the change is reflected immediately and persists across sessions.

**Acceptance Scenarios**:

1. **Given** an authenticated user with a previously recorded consent decision, **When** they open Settings > Cookie Preferences, **Then** they see their current per-category selections and the date/time they were last updated.
2. **Given** the Cookie Preferences section is open, **When** the user toggles a non-essential category off and saves, **Then** the change is persisted, takes effect immediately, and the "last updated" timestamp refreshes.
3. **Given** the Cookie Preferences section is open, **When** the user attempts to toggle the Essential category, **Then** the toggle is locked on and cannot be disabled.
4. **Given** the user saves a change to their cookie preferences, **When** the save request fails for any reason, **Then** the user sees a visible error message and their previous preferences remain in effect until they successfully retry.
5. **Given** the Cookie Preferences section is open, **When** the user clicks the Privacy link, **Then** they are taken to the Privacy Page.

---

### User Story 3 - Privacy Page Disclosure (Priority: P3)

Any visitor or user — logged in or not — can open a dedicated Privacy Page from the consent banner, the Cookie Preferences section, or the app's footer/navigation, and read a plain-language explanation of what cookies and data the platform collects, why, which third parties are involved, how long data is retained, and how to manage their preferences.

**Why this priority**: This is the transparency and legal-disclosure backbone that the banner and settings page both link to; it delivers standalone value (informing anyone who wants to understand the platform's data practices) even without the other two stories, but is lower priority than the mechanisms that actually act on consent.

**Independent Test**: Can be fully tested by navigating to the Privacy Page directly (without logging in) and confirming it renders with cookie category descriptions, third-party disclosures, retention information, and a working link/path to manage preferences.

**Acceptance Scenarios**:

1. **Given** any visitor (authenticated or not), **When** they navigate to the Privacy Page, **Then** the page loads without requiring login and displays cookie categories, their purposes, third-party services involved, and data retention information.
2. **Given** a user is on the consent banner, **When** they click the privacy link, **Then** they are taken to the Privacy Page.
3. **Given** a user is anywhere in the app footer/navigation, **When** they click the Privacy link, **Then** they are taken to the Privacy Page.
4. **Given** an authenticated user is on the Privacy Page, **When** they click the "manage your preferences" link, **Then** they are taken to Settings > Cookie Preferences.

---

### Edge Cases

- What happens when the cookie policy is updated to add a new category or a new policy version is published? The consent banner MUST reappear for all users (even those with a prior decision) so they can re-consent under the new terms, and it MUST again block interaction with the rest of the app until they do.
- How does the system handle a user who rejects all non-essential cookies — does core product functionality still work? Yes; only non-essential, category-linked functionality (e.g., analytics collection, marketing personalization) is affected, never core paid features.
- Is there any way to use the app before deciding, such as closing the tab and reopening it? No; the banner is re-shown and interaction remains blocked on every subsequent page load until an explicit decision is recorded — there is no time-boxed or partial-access bypass.
- What happens if a user changes their preference in Settings while the banner-driven decision from an earlier session is still cached somewhere (e.g., another open tab/device)? The most recent saved decision, tied to the account, always wins; the system does not maintain separate per-device consent state.
- What happens if a save or load of cookie preferences fails (network error, server error)? The user must see a visible, actionable error (e.g., a toast or inline message with a retry option) — never a silent failure — and the previously known consent state remains in effect.
- What happens on a user's very first visit before they log in (e.g., on the login/registration screens)? Only Essential cookies necessary to operate authentication are used; the consent flow governs the authenticated app experience reached after login.
- What happens if a user's role or account is deleted? Their cookie consent history is retained or deleted according to the same data-retention rules applied to the rest of their account data.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display a cookie consent banner on the main app page the first time an authenticated user logs in without a previously recorded consent decision for the current cookie policy version.
- **FR-002**: The consent banner MUST offer three explicit actions: Accept All, Reject Non-Essential, and Customize.
- **FR-003**: The Customize option MUST let the user toggle each cookie category independently: Essential (Strictly Necessary), Functional/Preferences, Analytics, and Marketing.
- **FR-004**: The Essential/Strictly Necessary category MUST always remain enabled and MUST NOT be toggleable off, in the banner or in Settings.
- **FR-005**: System MUST persist the user's consent decision (per category, plus the policy version consented to and a timestamp) tied to the user's account, not just their current browser or device.
- **FR-006**: System MUST NOT show the consent banner again after a user has recorded an explicit decision, unless a later condition (FR-007) requires re-consent.
- **FR-007**: System MUST re-trigger the consent banner for all users when a new cookie category is introduced or the cookie/privacy policy version changes.
- **FR-008**: The consent banner MUST include a visible link to the Privacy Page.
- **FR-009**: System MUST provide a public Privacy Page, accessible without authentication, describing: cookie categories and their purposes, categories of data collected, third-party services involved, data retention practices, and how to manage consent preferences.
- **FR-010**: The Privacy Page MUST be reachable from the consent banner, from the Cookie Preferences section, and from the application's footer/global navigation.
- **FR-011**: System MUST provide a Cookie Preferences section within the authenticated user's Settings area.
- **FR-012**: The Cookie Preferences section MUST display the user's current selection for each cookie category and the date/time preferences were last updated.
- **FR-013**: The Cookie Preferences section MUST let the user change and save their category selections at any time, with the Essential category always shown as locked on.
- **FR-014**: Changes saved from the Cookie Preferences section MUST take effect immediately (subsequent behavior of the app respects the new preferences without requiring further action from the user).
- **FR-015**: System MUST restrict access so a user can only view or modify their own cookie consent preferences, not another user's.
- **FR-016**: System MUST record when and to what state each consent decision changed, sufficient to answer "what was this user's consent state on date X" for audit/compliance purposes.
- **FR-017**: Any failure to load or save cookie consent preferences MUST be surfaced to the user through visible, actionable feedback (e.g., an error message with retry) — never a silent or console-only failure.
- **FR-018**: System MUST treat unauthenticated/pre-login pages as Essential-cookies-only, independent of the authenticated consent flow.
- **FR-019**: System MUST NOT activate any Functional, Analytics, or Marketing cookie/tracking activity for any user, regardless of the user's location, until that user has recorded an explicit consent decision (strict opt-in as the default legal posture — non-essential cookies are off until affirmatively turned on).
- **FR-020**: The consent banner MUST block interaction with the rest of the application — the user MUST NOT be able to dismiss it, navigate away, or use other app features — until they select Accept All, Reject Non-Essential, or Customize and save.
- **FR-021**: The Privacy Page and consent banner copy MUST be delivered in English only at initial launch; user-facing consent/privacy text MUST be centralized (not hardcoded inline per component) so future localization into additional languages does not require rearchitecting.

### Key Entities

- **Cookie Consent Record**: The account-level record of a user's current consent state — which categories are enabled, the policy version they consented under, and when the decision was last made or changed.
- **Cookie Category**: A named grouping of cookies with a shared purpose (Essential, Functional/Preferences, Analytics, Marketing), a description of its purpose, and whether it can be disabled by the user.
- **Cookie/Privacy Policy Version**: A versioned identifier for the published cookie/privacy terms; used to detect when existing users must be re-prompted for consent.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of users reaching the main app page for the first time (or without a recorded decision under the current policy version) are blocked by the consent banner, with zero non-essential cookie activity, until they record an explicit decision.
- **SC-002**: 0% of users see the consent banner reappear after recording an explicit decision, except when the cookie policy version changes.
- **SC-003**: Users can locate and update their cookie preferences from Settings in 3 clicks or fewer from the main app page.
- **SC-004**: 100% of saved cookie preference changes are reflected in subsequent app behavior without the user needing to reload the page or take any further action.
- **SC-005**: 100% of failed attempts to load or save cookie preferences result in a visible, user-facing error message (zero silent failures, measured via error-path test coverage).
- **SC-006**: The Privacy Page is reachable from the consent banner, the Cookie Preferences section, and global navigation in 1 click each.

## Assumptions

- The initial fixed set of cookie categories is: Essential (Strictly Necessary), Functional/Preferences, Analytics, and Marketing. Adding a new category in the future is treated as a policy-version change (FR-007).
- Consent is tied to the authenticated user's account rather than to a browser/device, so a decision made on one device is honored on all of the user's devices/sessions.
- Rejecting a non-essential category disables only the functionality directly tied to that category (e.g., analytics collection, marketing personalization) and never disables core, paid product functionality.
- Consent decision history is retained for the lifetime of the account (for audit/compliance purposes) and is handled under the same data-retention and deletion rules as the rest of the user's account data.
- Pre-login pages (login, registration, password reset) are out of scope for the customizable consent flow and are assumed to use only Essential cookies required for authentication to function.
- The Privacy Page's written content (legal copy) will be provided/reviewed by whoever owns compliance content for the platform; this specification defines where and how that content is presented and kept in sync with consent categories, not the legal wording itself.
- Localization of the Privacy Page and consent banner into languages beyond English is out of scope for this feature; it is expected as a fast-follow once the platform's broader i18n framework is in place.
- "Cookie Preferences section" (User Story 2) describes a distinct, dedicated area of Settings, not necessarily a separate route/URL — a tab or sub-panel within the existing Settings area satisfies FR-011 as long as it is reachable, viewable, and editable independently of the other Settings sections.
