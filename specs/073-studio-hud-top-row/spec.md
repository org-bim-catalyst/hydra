# Feature Specification: Studio HUD Top Row

**Feature Branch**: `073-studio-hud-top-row`

**Created**: 2026-09-25

**Status**: Draft

**Input**: User description: "Studio viewer top-left HUD layout refinement: (1) move the weather/temperature label so it sits inline to the right of the "Flumeria Studio" (project name) chip on the same top row, then the site-location card next to the temperature label on that same row; (2) in the site-location card remove the "Source: ..." provenance text entirely and keep only the first two lines — site name and confidence level; (3) color the shield icon in the site-location card by confidence level (e.g. High = green, Medium = amber, Low = red) so users can identify confidence at a glance by color, with the confidence text still present so color is not the only signal (accessibility)."

## Clarifications

### Session 2026-09-25

- Q: Should every confidence level use a shield icon, or keep today's per-level icons (Low is currently a question mark)? → A: A shield for every level, each with a distinct mark: shield + check (High), plain shield (Medium), shield + warning mark (Low).
- Q: How should the site card be styled next to its row neighbours? → A: The project name chip, weather label, and site card all share one frosted-glass style (the existing workspace-chrome surface, which updates with light/dark theme). All three are the same height, with content vertically centred. No left stripe.
- Q: Where does the Home button sit relative to the row? → A: The Home button leads the row. Home button, project name chip, weather label, and site card all sit on one horizontal line, sharing one vertical centreline.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Single-row workspace header (Priority: P1)

A user working in Flumeria Studio sees the Home button, the project name, the current weather, and the resolved site location as one horizontal row across the top-left of the map, in that order, instead of the Home/project pair with two cards stacked below it. The map area below the row is left clear.

**Why this priority**: The stacked layout eats vertical map space and pushes the site card well down the viewport. A single row is the main layout change requested and the one users notice first.

**Independent Test**: Open the studio with a resolved location and an active site boundary. Confirm the four items sit on one row, left to right: Home button → project name → weather → site location, sharing one vertical centreline, with nothing overlapping.

**Acceptance Scenarios**:

1. **Given** the studio is open with a resolved location and an active site boundary, **When** the page renders on a desktop-width viewport, **Then** the Home button, the project name chip, the weather label, and the site-location card appear on one row in that order, all the same height and sharing one vertical centreline, in one frosted-glass style, with consistent spacing between them.
2. **Given** the studio is open with a resolved location but no active site boundary, **When** the page renders, **Then** the row shows only the Home button, the project name chip, and the weather label, with no empty gap where the site card would be.
3. **Given** the studio is open and location has not resolved yet, **When** the page renders, **Then** the row shows only the Home button and the project name chip. When weather (and later a site boundary) arrives, each one joins the row in its place without moving the items already shown.
4. **Given** a site boundary appears or disappears during a session (for example, the user asks for a different site), **When** the row updates, **Then** the project name and weather stay where they are, and only the site card appears, changes, or goes away.
5. **Given** the row is visible, **When** the user switches between light and dark theme, **Then** all row items switch surface style together and still look identical to one another.

---

### User Story 2 - Compact site-location card (Priority: P2)

The site-location card shows only two lines: the site name and its confidence level. The provenance explanation ("Source: traced deterministically from…") and any other extra lines no longer appear on the card.

**Why this priority**: The provenance text makes the card tall and wordy, which blocks a single-row layout. The same explanation already appears in Lucy's chat reply, so the card doesn't need to repeat it.

**Independent Test**: Resolve any site boundary that has source details and alternative candidates. Confirm the card shows exactly the site name and confidence line and nothing else.

**Acceptance Scenarios**:

1. **Given** a site boundary is active and has source details, **When** the card renders, **Then** it shows the site name on the first line and the confidence level on the second line, and no source or provenance text.
2. **Given** a site boundary is active and the resolver also considered other candidate sites, **When** the card renders, **Then** the alternative candidate names are not shown on the card.
3. **Given** a site name too long for the card's maximum width, **When** the card renders, **Then** the name is shortened with an ellipsis on one line instead of wrapping onto a third line.

---

### User Story 3 - Confidence colour on the shield (Priority: P3)

The shield icon on the site-location card is coloured by confidence level: green for High, amber for Medium, red for Low. Users can tell at a glance, from across the screen, how far to trust the highlighted boundary.

**Why this priority**: This is a glanceability improvement that builds on the compact card. It is useful but not required for the layout change to work.

**Independent Test**: Resolve three sites that return High, Medium, and Low confidence. Confirm the icon colour is green, amber, and red respectively, in both light and dark theme.

**Acceptance Scenarios**:

1. **Given** an active boundary with High confidence, **When** the card renders, **Then** the icon is a green shield with a check mark and the second line reads "High confidence".
2. **Given** an active boundary with Medium confidence, **When** the card renders, **Then** the icon is a plain amber shield and the second line reads "Medium confidence".
3. **Given** an active boundary with Low confidence, **When** the card renders, **Then** the icon is a red shield with a warning mark and the second line reads "Low confidence — approximate".
4. **Given** any confidence level, **When** the user switches between light and dark theme, **Then** the icon colour still reads as the same green/amber/red and stays clearly visible against the card background.

---

### Edge Cases

- **Narrow viewports (phone / small tablet)**: If the row items don't fit on one row next to the top-right control cluster, the row wraps and the overflowing item moves onto a second line under the first. Items must never overlap each other or the top-right controls.
- **Stale weather reading**: The weather label's "last known reading" indicator MUST fit within the shared row height (for example, as a compact inline marker) rather than adding a line that makes the label taller than its neighbours.
- **Weather unavailable**: If the weather label shows its unavailable state, it keeps its place in the row. If it shows nothing, the site card moves left to sit directly after the project chip.
- **Project name**: The project name is a fixed label ("Flumeria Studio"), so it never needs truncating. The site card's own ellipsis (FR-008) keeps the row inside the viewport.
- **A row item fails to render**: One broken item must not take down the row, the chat, or the other controls. The item disappears, and the failure is shown to the user through the existing extension-failure notice (constitution §2.VIII).
- **Screen readers**: The card's accessible description still gives the site name and confidence level. Removing the visible source line does not remove the confidence meaning from what assistive technology announces.
- **Other overlays in the top-left area** (e.g. panels the viewer reserves space for): Their placement keeps respecting the row's new footprint. Content that avoided the old stacked cards now avoids the row instead.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The studio MUST show the Home button, the project name chip, the weather label, and the site-location card as one horizontal row anchored to the top-left of the workspace, in that order from left to right.
- **FR-002**: All row items MUST share one vertical centreline and one height. The three cards (project name, weather, site) are rounded rectangles of that height, with content vertically centred. The Home button stays circular, with a diameter equal to that height. Items are separated by one consistent gap that matches the spacing already used between the Home button and the project name chip today.
- **FR-002a**: The three cards MUST share one frosted-glass surface style: the same background, border, blur, corner radius, and shadow as the existing workspace chrome. The Home button keeps its existing matching circular chrome. That style MUST follow the app's light/dark theme, so all row items change together when the theme is switched. The site card MUST NOT have a left stripe or an accent-coloured border of its own.
- **FR-003**: Each row item MUST keep its current show/hide rules (weather only when a location has resolved, site card only when a boundary is active). An absent item MUST leave no gap.
- **FR-004**: When one item appears, disappears, or changes height, the items to its left MUST NOT move.
- **FR-005**: When the row is wider than the space available left of the top-right control cluster, the row MUST wrap onto more lines instead of overlapping other controls or overflowing the viewport.
- **FR-006**: The site-location card MUST show exactly two lines: the site name and the confidence level label.
- **FR-007**: The site-location card MUST NOT show the boundary's source/provenance text or the list of alternative candidate sites.
- **FR-008**: A site name longer than the card's width MUST be truncated with an ellipsis on a single line.
- **FR-009**: The site card's icon MUST be coloured by confidence level: green for High, amber for Medium, red for Low.
- **FR-010**: The site card's icon MUST be a shield at every confidence level, with a distinct mark per level: shield + check (High), plain shield (Medium), shield + warning mark (Low). Confidence MUST NOT be conveyed by colour alone. The distinct mark and the textual confidence label both MUST stay.
- **FR-011**: Each confidence colour MUST reach at least 3:1 contrast against the shared frosted-glass surface in both light and dark theme (WCAG 2.1 AA non-text contrast).
- **FR-012**: The site card's accessible name MUST still include the site name and confidence level. It no longer needs to include the source.
- **FR-013**: Interactivity MUST be unchanged: the Home button stays clickable and keeps its hover feedback and landing-page navigation; clicks and drags on the weather label and site card go through to the map, so the map remains usable underneath.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a 1280 px-wide or wider viewport, the Home button, project name, weather, and site card all sit on one row, sharing one vertical centreline. The row's total height is no taller than its tallest item, freeing at least 90 px of vertical map area on the left edge compared with the current stacked layout.
- **SC-002**: The site-location card's height drops to two text lines for 100% of resolved boundaries, whatever their source or alternative candidates.
- **SC-003**: In a quick glance test (under 2 seconds of exposure), users correctly name the confidence level from the icon colour for at least 90% of cards shown.
- **SC-004**: An automated test measures each of the three confidence colours at ≥ 3:1 contrast against the card surface, in both light and dark theme. Automated accessibility checks report no new violations on the changed components.
- **SC-005**: No overlap between row items and between the row and the top-right controls at viewport widths from 360 px to 2560 px.

## Assumptions

- "Flumeria Studio" in the request means the existing project name chip in the studio's top-left corner, next to the Home button. The content and behaviour of both don't change; only their size, where needed to match the shared row height, and their neighbours do.
- Provenance and alternative-candidate information are removed only from the card. They stay in the underlying data and in Lucy's chat reply, so nothing is lost for users who want the detail.
- Green/amber/red come from the app's existing success/warning/error semantic palette rather than new custom colours, so they match the rest of the product in both themes.
- The row applies only to the studio workspace. Other pages are unaffected.
