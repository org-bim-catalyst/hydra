# DESIGN_SYSTEM.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.0
>
> **Framework:** React 19 + Material UI v7
>
> **Theme Engine:** Material UI + CSS Variables
>
> **Icon Library:** Material Symbols Rounded
>
> **Typography:** Inter
>
> **Last Updated:** July 2026

---

# 1. Purpose

The Ask Lucy Design System is the **single source of truth** for every visual element in the application.

It defines:

* Design Tokens
* Color System
* Typography
* Layout Grid
* Component Library
* Motion
* Accessibility
* Responsive Rules
* Interaction Patterns

Every screen should be composed entirely from reusable design system components.

Never create one-off UI elements when an existing component can be extended.

---

# 2. Design Philosophy

The design language should communicate:

* Intelligence
* Simplicity
* Precision
* Professionalism
* Trust
* Speed

The application should feel like a premium AI productivity suite.

Influences include:

* ChatGPT
* Claude
* Linear
* Notion
* GitHub
* VS Code
* Material Design 3

Avoid excessive decoration or skeuomorphic effects.

---

# 3. Design Principles

## Clarity

Every interface should communicate one primary purpose.

---

## Consistency

Similar actions must always look identical.

---

## Predictability

Buttons, dialogs, and navigation behave consistently across all modules.

---

## Efficiency

Optimize for power users.

Support keyboard shortcuts throughout the application.

---

## Progressive Disclosure

Hide advanced functionality until needed.

---

## Accessibility

Every component must comply with WCAG 2.2 AA.

---

# 4. Design Tokens

All visual values originate from design tokens.

Never hardcode values.

```text
Color

Typography

Spacing

Elevation

Radius

Animation

Opacity

Border

Shadow

Breakpoint

Z-Index
```

Expose tokens through the MUI theme.

---

# 5. Color Palette

## Primary

Used for:

* Primary buttons
* Active navigation
* Links
* Selected items

---

## Secondary

Used for:

* Supporting actions
* Chips
* Highlights

---

## Semantic Colors

```text
Success

Warning

Error

Info
```

Never use arbitrary colors.

---

## Neutral Palette

```text
Gray 50

Gray 100

Gray 200

Gray 300

Gray 400

Gray 500

Gray 600

Gray 700

Gray 800

Gray 900
```

Neutrals define the majority of the interface.

---

# 6. Surface Hierarchy

```text
Background

↓

Surface

↓

Card

↓

Dialog

↓

Popover

↓

Tooltip
```

Each level increases visual elevation.

---

# 7. Elevation

Use minimal shadows.

Recommended levels

```text
0

1

2

4

8

16
```

Avoid large shadows.

---

# 8. Border Radius

Standardize radii.

```text
XS = 4

SM = 8

MD = 12

LG = 16

XL = 24

Pill = 999
```

Never invent custom radii.

---

# 9. Spacing Scale

Use an 8px spacing system.

```text
4

8

16

24

32

40

48

64

80

96
```

No arbitrary spacing values.

---

# 10. Grid System

Desktop

12-column grid

Tablet

8-column grid

Mobile

4-column grid

Use CSS Grid for layouts and Flexbox for component alignment.

---

# 11. Breakpoints

```text
xs

sm

md

lg

xl
```

Follow Material UI defaults unless project-specific overrides are required.

---

# 12. Typography

Primary font

Inter

Fallback

```text
Segoe UI

Roboto

Arial

sans-serif
```

Code font

```text
JetBrains Mono

Cascadia Code

Consolas

monospace
```

---

# 13. Typography Scale

```text
Display

Headline

Title

Body

Label

Caption

Code
```

Use theme typography variants consistently.

---

# 14. Iconography

Use Material Symbols Rounded exclusively.

Sizes

```text
16

20

24

32

40
```

Icons should reinforce actions, not decorate them.

---

# 15. Motion

Animations should be subtle.

Durations

```text
100ms

150ms

200ms

250ms

300ms
```

Easing

Use Material motion curves.

Never exceed 300ms for routine interactions.

---

# 16. Light & Dark Themes

Support:

* Light
* Dark
* System

Theme changes should occur instantly without page reloads.

Persist theme preference per user.

---

# 17. Theme Architecture

Create a single theme factory.

```text
theme/

tokens/

palette.ts

typography.ts

spacing.ts

shadows.ts

components.ts

index.ts
```

All theme customization should live here.

---

# 18. Component Categories

## Foundations

* Typography
* Icons
* Colors
* Grid
* Spacing

---

## Inputs

* Button
* TextField
* Select
* Checkbox
* Switch
* Slider
* Autocomplete
* Date Picker
* File Upload
* Search

---

## Data Display

* Card
* Table
* DataGrid
* List
* Badge
* Avatar
* Chip
* Timeline
* Tree View
* Progress

---

## Navigation

* Sidebar
* Tabs
* Breadcrumbs
* Drawer
* Pagination
* Stepper
* Menu

---

## Feedback

* Snackbar
* Alert
* Dialog
* Tooltip
* Loading Overlay
* Skeleton
* Progress Bar

---

## AI Components

* Chat Bubble
* Prompt Composer
* Streaming Message
* Token Counter
* Model Badge
* Provider Badge
* Knowledge Badge
* Citation Card
* Tool Execution Panel
* Thinking Indicator

These are first-class design system components, not page-specific widgets.

---

# 19. Button Standards

Variants

* Filled
* Outlined
* Text
* Icon

Sizes

* Small
* Medium
* Large

States

* Default
* Hover
* Focus
* Active
* Disabled
* Loading

Never use custom button styles outside the design system.

---

# 20. Form Standards

Every form includes:

* Label
* Helper text
* Validation
* Required indicator
* Error message

Validation appears inline.

Do not rely solely on toast notifications.

---

# 21. Dialog Standards

Maximum sizes

* Small
* Medium
* Large
* Fullscreen

Every dialog includes:

* Title
* Description (optional)
* Close button
* Primary action
* Secondary action

Support `Esc` to close where appropriate.

---

# 22. Cards

Cards represent discrete objects.

Examples

* Conversation
* Agent
* Knowledge Base
* Document
* Prompt

Cards should have consistent spacing, elevation, and action placement.

---

# 23. Tables

Use MUI DataGrid.

Standards

* Sorting
* Filtering
* Column resize
* Density selector
* Export (where applicable)
* Virtualization
* Sticky headers

Large datasets must use server-side pagination.

---

# 24. Navigation

Sidebar

* Collapsible
* Keyboard accessible
* Persistent on desktop
* Drawer on mobile

Top bar

* Global search
* Notifications
* Active model
* User menu

---

# 25. Chat Components

Standard components

```text
ConversationList

ConversationItem

ChatHeader

MessageBubble

StreamingBubble

PromptComposer

AttachmentChip

ThinkingIndicator

CitationList

MessageToolbar
```

All chat pages reuse these components.

---

# 26. AI Model Selector

Component

`ModelSelector`

Features

* Provider grouping
* Search
* Capability badges
* Context window
* Favorites
* Recently used

Support keyboard navigation.

---

# 27. File Upload

Unified upload component.

Supports

* Drag and drop
* Browse
* Paste
* Upload progress
* Validation
* Retry
* Cancel

Used throughout the application.

---

# 28. Empty States

Every module defines an empty state.

Includes

* Illustration (optional)
* Title
* Description
* Primary action

No blank pages.

---

# 29. Loading States

Preferred order

1. Skeleton
2. Progress
3. Spinner

Avoid full-page blocking loaders.

---

# 30. Error States

Every error includes

* Friendly title
* Explanation
* Retry
* Technical details (optional)
* Correlation ID (advanced)

---

# 31. Notifications

Snackbar

* Success
* Warning
* Error
* Info

Auto-dismiss after a reasonable duration except for critical failures.

---

# 32. Accessibility Standards

Every component must support

* Keyboard navigation
* Focus ring
* Screen readers
* ARIA labels
* Reduced motion
* High contrast mode

Never remove focus outlines without providing an accessible replacement.

---

# 33. Keyboard Navigation

Global shortcuts

```text
Ctrl + K

Ctrl + N

Ctrl + ,

Ctrl + /

Esc

Enter

Shift + Enter
```

Every major workflow should be fully keyboard operable.

---

# 34. State Indicators

Use consistent visual indicators for:

* Active
* Selected
* Disabled
* Loading
* Error
* Success
* Streaming
* Synced
* Offline (future)

---

# 35. Component Naming

Naming convention

```text
BaseButton

PrimaryButton

ConversationCard

KnowledgeCard

ModelSelector

PromptComposer

StreamingMessage

UserAvatar

SidebarNavigation
```

Names should reflect responsibility, not appearance.

---

# 36. Folder Structure

```text
src/

components/

atoms/

molecules/

organisms/

layouts/

providers/

theme/

hooks/

utils/
```

Feature-specific wrappers belong under each feature module; generic components remain in the shared library.

---

# 37. Storybook

Every reusable component must include:

* Story
* Variants
* States
* Accessibility checks
* Documentation
* Usage examples

Storybook is the visual contract for the design system.

---

# 38. Figma Synchronization

Maintain parity between:

* Design tokens
* Component variants
* Naming
* States
* Documentation

The implementation should match approved designs.

---

# 39. Performance Standards

Components should:

* Minimize unnecessary renders
* Memoize expensive computations
* Support virtualization for long lists
* Avoid large bundle dependencies

Measure performance regularly.

---

# 40. Testing Standards

Each reusable component should include:

* Unit tests
* Accessibility tests
* Interaction tests
* Visual regression tests (future)

Critical workflows should also have Playwright end-to-end coverage.

---

# 41. AI-Specific UX Standards

When AI is working, always communicate status.

Examples

* Generating response
* Searching knowledge base
* Running tool
* Calling external service
* Creating embeddings
* Indexing document

Never leave users wondering whether the application is still working.

---

# 42. Component Inventory

The initial design system should provide approximately:

| Category           | Target Components |
| ------------------ | ----------------: |
| Foundations        |                12 |
| Inputs             |                18 |
| Navigation         |                12 |
| Data Display       |                22 |
| Feedback           |                15 |
| AI Components      |                20 |
| Layout Components  |                10 |
| Utility Components |                15 |

**Total Target:** 100+ reusable components.

---

# 43. Versioning

The design system follows semantic versioning.

* Major: Breaking visual or API changes
* Minor: New components or variants
* Patch: Bug fixes and refinements

Applications should consume a single version of the design system at a time.

---

# 44. Governance

All new components must:

* Solve a reusable problem
* Follow naming conventions
* Use design tokens only
* Include documentation
* Include Storybook stories
* Include automated tests
* Pass accessibility review
* Be approved before widespread adoption

Avoid duplicate components with overlapping responsibilities.

---

# 45. Design System Checklist

Before releasing any UI change, verify:

* Does it use existing design tokens?
* Does it reuse existing components where possible?
* Is it responsive across supported breakpoints?
* Is it accessible (WCAG 2.2 AA)?
* Does it support both light and dark themes?
* Is it keyboard accessible?
* Does it include loading, empty, and error states?
* Is it documented in Storybook?
* Is it covered by automated tests?
* Does it maintain visual consistency with the rest of the application?

The Ask Lucy Design System is a strategic asset. It should evolve deliberately, emphasizing consistency, usability, accessibility, and long-term maintainability over short-term convenience.
