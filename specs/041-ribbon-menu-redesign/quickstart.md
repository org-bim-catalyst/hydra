# Quickstart / Validation Guide: Ribbon Menu Redesign

## Prerequisites

- App running: `cd src/AskLucy.Web/ClientApp && npm run dev` (or full stack via `dotnet run`)
- Navigate to a workspace view that renders the immersive viewer (the map/GIS view with floating controls)

## Validation Scenarios

### 1. Collapsed button color

- **Expected**: Every floating circular button (Layers, Navigation, Selection, Analysis, Map style, View mode) shows a dark gray circle — `#45454D`.
- **Check**: Inspect the Fab background in DevTools → matches `#45454D`.

### 2. Right-side button → horizontal ribbon to the left

- **Action**: Click any `right-stack` button (Layers, Navigation, Selection, Analysis, Map style, View mode).
- **Expected**:
  - A single horizontal rounded-rectangle pill appears to the **left** of the trigger.
  - The pill contains all option icons in one row.
  - The trigger button turns **green** (`#2E7F26`).
- **Check**: The pill is a single row — no separate header row above the icons.

### 3. Active option purple highlight

- **Action**: Expand the Map style control. The currently selected style (e.g. Roadmap) should appear purple.
- **Expected**: Active option has a `#9C62DE` circular background and white icon. All other options have the normal translucent dark background.

### 4. Collapse on click away / Escape

- **Action**: With a ribbon open, click on the map surface outside the pill.
- **Expected**: Ribbon closes, trigger returns to `#45454D`.
- **Action**: Open ribbon, press Escape.
- **Expected**: Ribbon closes, focus returns to the trigger button.

### 5. One ribbon at a time

- **Action**: Open Layers, then click Navigation.
- **Expected**: Layers ribbon closes before Navigation opens.

### 6. Account menu unchanged

- **Action**: Click the Account button (top-right).
- **Expected**: The existing icon+label list appears below — no ribbon, no color changes to the account button.

### 7. Bottom-end button → ribbon upward (if applicable)

- **Action**: Click any `bottom-end` control (e.g. the chat panel trigger).
- **Note**: The chat panel uses `FloatingPanel`, not `ExpandableActionGroup`. If a `bottom-end` action-group control exists, its ribbon should appear **above** the trigger.

## Regression Checks

- Keyboard navigation through ribbon options (Tab order correct).
- `inert` attribute prevents interaction with collapsed content (verify in DevTools).
- No horizontal scrollbar on the workspace surface (ribbon should not overflow the viewport).
