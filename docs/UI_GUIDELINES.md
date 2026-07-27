# UI_GUIDELINES.md

> **Project:** Ask Lucy AI Workspace
>
> **Frontend Stack:** React 19 + TypeScript + Vite + Material UI (MUI)
>
> **Architecture:** Feature-Based + Atomic Components
>
> **State Management:** Zustand + TanStack Query
>
> **Forms:** React Hook Form + FluentValidation-compatible DTOs
>
> **Last Updated:** July 2026

---

# 1. Vision

Ask Lucy is **not just another AI chatbot**.

It is an **AI Productivity Workspace** where users can:

* Chat with multiple AI models
* Manage conversations
* Build knowledge bases
* Create AI agents
* Manage prompts
* Upload documents
* Switch LLMs instantly
* Collaborate (future)
* Automate workflows (future)

The interface should feel like a combination of:

* ChatGPT
* Claude
* Notion
* GitHub
* VS Code

while remaining clean, professional and enterprise-ready.

---

# 2. UI Design Principles

Every screen should follow these principles.

## Simplicity

Remove unnecessary controls.

Only show what the user needs.

---

## Progressive Disclosure

Advanced AI options remain hidden until requested.

Example:

Basic Mode

* Message input
* Attach file
* Send

Advanced Mode

* Temperature
* Top P
* Model
* Provider
* System Prompt
* RAG options

---

## Fast Navigation

Users should reach any feature within three clicks.

---

## Workspace-Oriented

The application is a workspace—not a wizard.

Users can freely switch between:

* Chats
* Knowledge
* Agents
* Settings
* Billing

without losing context.

---

## Accessibility

Every feature must support:

* Keyboard navigation
* Screen readers
* High contrast
* WCAG 2.2 AA

---

# 3. Design Language

Visual identity should communicate:

* Intelligence
* Clarity
* Trust
* Productivity
* Speed

Avoid playful consumer UI.

Favor enterprise-grade design.

---

# 4. Layout System

Desktop

```text
──────────────────────────────────────────────────────────
 Top Navigation
──────────────────────────────────────────────────────────

 Sidebar │ Workspace │ Right Panel

 Sidebar │ Workspace │ Inspector

 Sidebar │ Workspace │ AI Settings

──────────────────────────────────────────────────────────
```

---

Mobile

```text
Top Bar

Workspace

Bottom Navigation
```

Sidebar becomes a slide-out drawer.

---

# 5. Responsive Breakpoints

```text
xs 0

sm 600

md 900

lg 1200

xl 1536
```

Layouts must adapt smoothly without horizontal scrolling.

---

# 6. Global Navigation

Left Sidebar

* New Chat
* Conversations
* Knowledge Bases
* Prompt Library
* Agents
* Files
* Usage
* Billing
* Settings
* Administration (authorized users)

Bottom

* User Avatar
* Theme Toggle
* Logout

Sidebar should be collapsible.

---

# 7. Top Navigation

Contains:

* Workspace title
* Search
* Notifications
* Active AI model
* Current provider
* User profile

Optional global command button.

---

# 8. Global Search

Shortcut

```text
Ctrl + K
```

Searches:

* Chats
* Documents
* Knowledge Bases
* Prompts
* Agents
* Files

Future

Semantic search.

---

# 9. Dashboard

The landing page should present a productivity dashboard rather than an empty screen.

Widgets

* Recent chats
* Favorite prompts
* Knowledge bases
* Active agents
* AI usage
* Storage usage
* Subscription
* Recent uploads

---

# 10. Chat Workspace

Primary screen.

Layout

```text
Conversation List

↓

Messages

↓

Prompt Composer
```

Messages occupy the majority of available width while maintaining readable line lengths.

---

# 11. Conversation Sidebar

Features

* Search
* New Chat
* Folders (future)
* Pin
* Favorite
* Archive
* Delete
* Rename

Grouping

Today

Yesterday

Last 7 Days

Last Month

Older

---

# 12. Conversation Cards

Display

* Title
* Last message preview
* AI model badge
* Timestamp
* Pin indicator
* Favorite indicator

Context menu

Rename

Duplicate

Archive

Delete

Export

---

# 13. Chat Header

Displays

Conversation title

Attached knowledge bases

Current AI model

Provider

Temperature (optional)

Streaming status

---

# 14. Message Design

Each message includes

Avatar

Author

Timestamp

Markdown

Code blocks

Tables

Images

Citations

Streaming animation

Actions

Copy

Regenerate

Edit prompt

Retry

Delete

Share (future)

---

# 15. Markdown Rendering

Support

* Headings
* Lists
* Tables
* Task lists
* Blockquotes
* Code fences
* Mermaid diagrams (future)
* LaTeX
* Inline math

Code blocks

* Syntax highlighting
* Copy button
* Line numbers (optional)
* Language badge

---

# 16. Prompt Composer

Features

* Auto-growing text area
* Drag-and-drop files
* Voice input
* Image upload
* Paste screenshots
* Mention knowledge bases
* Mention agents
* Keyboard shortcuts

Enter

Send

Shift + Enter

New line

---

# 17. Streaming UX

While generating:

* Token-by-token rendering
* Typing indicator
* Cancel generation
* Retry
* Token counter
* Elapsed time

Avoid rendering delays.

---

# 18. Attachments

Supported

PDF

DOCX

Images

CSV

Markdown

Audio

Display

Preview

Progress bar

Status

Remove

---

# 19. Knowledge Base UI

Main page

Knowledge Base Cards

Each card displays

Name

Documents

Storage

Last Updated

Visibility

Click opens workspace.

---

# 20. Document Workspace

Split layout

Document list

↓

Metadata panel

↓

Processing status

Status indicators

Uploaded

Parsing

Chunking

Embedding

Indexed

Failed

---

# 21. Document Viewer

Features

Original document preview

Extracted text

Chunk viewer

Metadata

Embedding status

Re-index button

---

# 22. Prompt Library

Grid or table view.

Displays

Category

Title

Variables

Favorites

Version

Actions

Run

Duplicate

Edit

Delete

---

# 23. Agent Builder

Layout

Agent Information

↓

Instructions

↓

Available Tools

↓

Knowledge Bases

↓

Preferred Model

↓

Temperature

↓

Test Console

Agent creation should feel similar to configuring a GPT or Claude Project.

---

# 24. AI Settings

Organized into tabs.

General

Provider

Model

Generation

Memory

Voice

Advanced

Examples

Provider

OpenAI

Anthropic

Google

OpenRouter

Local

Model dropdown dynamically updates based on selected provider.

---

# 25. Provider Switching

Changing providers should never require a page refresh.

The UI should immediately:

Update model list

Validate capabilities

Persist preference

---

# 26. Memory Manager

Displays

Remembered facts

Preferences

Projects

Writing style

Users can:

Edit

Delete

Pin

Forget

---

# 27. Usage Dashboard

Charts

Requests

Tokens

Latency

Provider usage

Model usage

Storage

Costs (future)

---

# 28. Billing

Displays

Current plan

Storage

Tokens

Usage

Invoices

Payment history

Upgrade button

---

# 29. User Profile

Tabs

Profile

Security

API Keys (future)

Connected Accounts

Sessions

Notifications

---

# 30. Administration

Only visible to authorized users.

Modules

Users

Providers

Models

Logs

Feature Flags

System Settings

Audit

Background Jobs

---

# 31. Notifications

Types

Success

Warning

Error

Information

Use unobtrusive snackbars for transient events and notification center for persistent items.

---

# 32. Loading States

Never leave blank screens.

Use:

* Skeleton loaders
* Progress bars
* Shimmer placeholders
* Upload indicators
* Streaming indicators

---

# 33. Empty States

Every empty page should include:

Illustration (optional)

Description

Primary action

Examples

"No conversations yet."

Create New Chat

---

# 34. Error States

Provide

Friendly explanation

Retry button

Technical details (expandable)

Correlation ID

---

# 35. Theme System

Support

Light

Dark

System

Theme switching should be instantaneous and persist per user.

---

# 36. Color Tokens

Never hardcode colors.

Use semantic tokens.

Examples

```text
Primary

Secondary

Surface

Background

Border

Success

Warning

Error

Info
```

MUI theme is the single source of truth.

---

# 37. Typography

Recommended font stack

```text
Inter

Segoe UI

Roboto

System UI
```

Hierarchy

Display

Heading

Title

Body

Caption

Code

Code uses a monospace font.

---

# 38. Icons

Use Material Symbols or Lucide consistently.

Avoid mixing icon libraries.

Icons should communicate meaning without decorative excess.

---

# 39. Motion

Use subtle animations.

Examples

Fade

Slide

Expand

Collapse

Streaming cursor

Avoid excessive motion that distracts from reading.

---

# 40. State Management

## Zustand

Global client state

* Authentication
* Theme
* Sidebar
* User preferences
* Active conversation
* Active provider

## TanStack Query

Server state

* Conversations
* Messages
* Models
* Documents
* Agents
* Knowledge bases

Avoid duplicating server state in Zustand.

---

# 41. Folder Structure

```text
src/

app/
components/
features/
hooks/
layouts/
pages/
providers/
routes/
services/
store/
theme/
types/
utils/
```

Feature example

```text
features/chat/

api/
components/
hooks/
pages/
types/
utils/
```

Shared UI components belong under `components/`.

---

# 42. Component Architecture

Follow Atomic Design.

Atoms

* Button
* Icon
* Avatar
* Badge
* Chip

Molecules

* Search Box
* Message Bubble
* Provider Selector

Organisms

* Conversation Sidebar
* Prompt Composer
* Knowledge Explorer

Templates

* Dashboard Layout
* Chat Layout
* Settings Layout

Pages

* Chat
* Dashboard
* Settings

---

# 43. Forms

Use

React Hook Form

Validation

Zod on the client

FluentValidation on the server

Display validation inline.

---

# 44. Performance

Target

* Initial load under 2 seconds
* Route-based code splitting
* Lazy loading
* Virtualized conversation lists
* Virtualized long chats
* Image lazy loading
* Memoized expensive components

---

# 45. Accessibility

Every interactive element must include:

* Keyboard support
* Focus indicators
* Accessible labels
* Proper ARIA attributes
* Sufficient color contrast

Never rely solely on color to convey meaning.

---

# 46. Keyboard Shortcuts

| Shortcut         | Action             |
| ---------------- | ------------------ |
| Ctrl + K         | Global Search      |
| Ctrl + N         | New Chat           |
| Ctrl + Shift + U | Upload Document    |
| Ctrl + ,         | Settings           |
| Ctrl + /         | Keyboard Shortcuts |
| Esc              | Close Dialog       |
| Enter            | Send Message       |
| Shift + Enter    | New Line           |

---

# 47. Internationalization

Support:

* Unicode
* RTL languages
* Locale-aware dates
* Locale-aware numbers
* Multiple time zones

RTL support should automatically mirror layouts where appropriate.

---

# 48. Future UI Modules

The architecture should accommodate:

* Team workspaces
* Shared chats
* Shared knowledge bases
* Workflow designer
* Prompt marketplace
* Agent marketplace
* Voice workspace
* Meeting intelligence
* BIM workspace
* Autodesk integrations
* Microsoft 365 integrations

New modules should plug into the existing navigation without redesigning the application.

---

# 49. UX Principles for AI

The AI should always make its reasoning transparent where appropriate.

Provide clear indicators for:

* Streaming response
* Tool execution
* Knowledge retrieval
* Model used
* Token usage
* Attached knowledge sources
* Confidence or citations when available

The interface should help users understand *how* an answer was produced without overwhelming them.

---

# 50. UI Quality Checklist

Before releasing any feature, verify:

* Is the workflow intuitive?
* Is it responsive on desktop and mobile?
* Is keyboard navigation complete?
* Are loading, empty, and error states implemented?
* Does it meet WCAG 2.2 AA?
* Is the design consistent with the design system?
* Does it avoid unnecessary clicks?
* Is performance acceptable?
* Is state managed correctly?
* Are all user preferences persisted?
* Does it support future extensibility without redesign?

The Ask Lucy UI should feel like a modern AI operating system: fast, focused, extensible, and consistent across every feature.
