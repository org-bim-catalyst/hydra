# Feature Specification: Notifications & Communication Hub

**Feature Branch**: `067-notifications-communication-hub`

**Created**: 2026-09-22

**Status**: Draft

**Input**: User description: "SPEC-011 — Notifications & Communication Hub: build a centralized Notifications & Communication Hub for Ask Lucy — one consistent abstraction through which every platform component (agents, workflows, approvals, documents, knowledge bases, account/security, system) communicates with users, initially via in-app notifications and email (SMTP STARTTLS through the existing myasp.net mail service), extensible to WhatsApp, Teams, Slack, SMS, push and webhooks later. Senders must not know which channel or provider delivers a notification. Includes user preferences, versioned templates, localization (English/Arabic RTL), reliable asynchronous delivery with retry, dead-letter and idempotency, admin monitoring, and strict content/link/header security." (Source: `_misc/specs/SPEC-011 — Notifications & Communication Hub.md`)

## Clarifications

### Session 2026-09-23

- Q: Should existing notification sources (document-processing and memory in-app notifications, and the branded account emails from spec 061) be migrated onto the hub, or left as-is? → A: Migrate all of them — document and memory notifications and account emails all move onto the hub in this feature.
- Q: How much Arabic does this feature deliver? → A: Full Arabic — Arabic versions of every default template plus an Arabic notification center and preferences interface. English remains the main language of the website and of all messages, emails and notifications; Arabic is used only when localization is enabled or the user chooses to switch language.
- Q: Is the user's language switch gated by the administrator's localization setting, and are admin screens included in the Arabic scope? → A: Yes, the user switch is available only while localization is enabled. When enabling localization, the administrator also chooses which languages are supported (English always included), and users can switch only among those. The administrator notification screens are also available in Arabic. Memory email stays off by default.
- Q: Which admin screens are in the Arabic scope? → A: The entire admin area, not only the notification screens: the page shell (title, sidebar, header) and every admin section — Dashboard, Users, Roles, Role assignments, System agents, AI providers, Default models, AI capabilities, Agent policies, Workflow policies, MCP servers, Jobs — plus the new notification admin screens. English names, brand and product names, acronyms and abbreviations are never translated (e.g., OpenAI, Anthropic, AI, MCP, 2FA).
- Q: Should system announcements be sent by email? → A: Every announcement appears in-app. Email goes out only for announcements an administrator marks as critical, sent at a limited rate, and users can turn that email off (System email is never mandatory).
- Q: Can administrators edit the account and security email templates (password reset, email confirmation, new-email confirmation, password-changed, support request)? → A: Yes — they are editable like any other template; the declared-variable rule (FR-041) and trusted-link rule (FR-047) are the only safeguards, with no extra locks or elevated publish permission.
- Q: What does deleting a notification do? → A: Deleting hides the notification from the user immediately; retention cleanup removes the record permanently later. Every category can be deleted, including approval and security, because their audit history is kept separately.
- Q: Which permissions control the new notification admin screens? → A: One new "Notifications" area in the spec-055 permission catalogue with View and Manage. View covers every notification admin screen read-only; Manage covers retries, template editing and publishing, system announcements and localization settings.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Notification Center: see and manage my notifications (Priority: P1)

A signed-in user sees a notification bell with an unread count anywhere in the app. Opening it shows their notifications newest-first, grouped by date, each with a title, short message, category, priority indicator and read/unread state. They can open a notification to see its details, follow its related action to the item it concerns (a workflow run, an agent run, a document), mark one or all as read, and delete notifications they no longer need. New notifications appear without refreshing the page.

**Why this priority**: This is the minimum viable hub — without a place to receive and act on notifications, no other channel, preference or template has anything to attach to. It alone delivers visible value as soon as any module emits a notification.

**Independent Test**: Seed notifications for a user (read, unread, several categories and priorities), then verify the bell count, list, grouping, filtering, detail view, related-action navigation, mark-read, mark-all-read, delete, pagination, and live arrival of a newly created notification — with no email or preference functionality present.

**Acceptance Scenarios**:

1. **Given** a user has 3 unread and 5 read notifications, **When** they view any page, **Then** the bell shows an unread count of 3.
2. **Given** the notification list is open, **When** a new notification is created for that user, **Then** it appears at the top of the list and the unread count increments without a page refresh.
3. **Given** an unread notification, **When** the user marks it as read, **Then** it shows as read and the unread count decreases by one.
4. **Given** several unread notifications, **When** the user chooses "mark all as read", **Then** every notification shows as read and the unread count is 0.
5. **Given** a notification about a workflow run, **When** the user activates its related action, **Then** they are taken to that workflow run inside Ask Lucy — and, if they are no longer authorized to see it, they see an access-denied message rather than the content.
6. **Given** a user deletes a notification, **When** they reload the notification center (on any device), **Then** it no longer appears in the list, cannot be opened, and no longer counts toward the unread count.
7. **Given** a user has no notifications, **When** they open the notification center, **Then** they see a clear empty state; **Given** loading fails, **Then** they see an error message with a retry option.
8. **Given** a user filters by category (e.g., Workflow) or by unread only, **When** the filter is applied, **Then** only matching notifications are listed and further pages load on demand.
9. **Given** two different users, **When** either views or acts on notifications, **Then** neither can see, read, mark or delete the other's notifications, including by guessing an identifier.

---

### User Story 2 - Be told when platform work finishes, fails or needs me (Priority: P1)

A user who starts an agent run, a workflow, or a document upload moves on to other work. When that work completes, fails, pauses, or requires their approval — or when a knowledge base finishes or fails indexing, their account's security settings change, or an administrator announces maintenance — they receive a notification through the hub. The module that raised the event never decides how the user is reached; the hub does.

**Why this priority**: Long-running, asynchronous work (agents, workflows, document processing, indexing) is the main reason users need notifications at all. Without event sources the notification center is empty.

**Independent Test**: Trigger each supported event type (agent completed/failed/needs approval, workflow started/completed/failed/paused/needs approval, document processed/failed/OCR completed/indexed/index failed, knowledge base indexed/index failed, account security change, system announcement) and verify exactly one correctly categorized, correctly prioritized, correctly linked in-app notification is created for the right recipient.

**Acceptance Scenarios**:

1. **Given** a user's workflow run completes, **When** the completion event is raised, **Then** the user receives one Workflow-category notification linking to that run.
2. **Given** an agent run fails, **When** the failure event is raised, **Then** the run's owner receives a High-priority Agent-category notification including the agent's name and a link to the run, without internal error details.
3. **Given** a document fails processing, **When** the failure event is raised, **Then** the document owner receives a Document-category notification naming the document.
4. **Given** a feature module raises an event, **When** notification delivery fails or is slow, **Then** the originating operation (the workflow, agent run, upload) is not failed, rolled back or delayed by it.
5. **Given** an important event was recorded, **When** the process handling the user's request crashes immediately afterwards, **Then** the notification is still delivered once the system recovers.
6. **Given** an administrator publishes a system announcement (maintenance or service degradation), **When** it is published, **Then** every targeted active user receives a System-category in-app notification; only if the administrator marked the announcement critical is it also emailed, at a limited sending rate, to those targeted users who have not turned off System email.

---

### User Story 3 - Receive important notifications by email (Priority: P2)

A user receives important notifications — approval requests, agent and workflow outcomes, security and account events — by email as well as in the app. Emails are branded, readable on phone and desktop, in light and dark mail clients, include a plain-text alternative, and link back to the right place in Ask Lucy. Sensitive events are announced by email without revealing sensitive details ("You have a new security notification. Sign in to Ask Lucy to view it."). An email is never sent twice for the same notification, and a temporary mail-service outage does not lose it.

**Why this priority**: Email reaches users who are not currently in the app — essential for approvals and failures — but in-app delivery (US1) and event sourcing (US2) must exist first.

**Independent Test**: With a test mail server, raise events in email-enabled categories and verify: one email per notification, correct subject/HTML/plain-text, safe links, minimized content for sensitive categories; then simulate a mail-service outage, a delivery-worker restart mid-send and concurrent workers, and verify retries, no duplicates, and eventual dead-letter after the retry limit.

**Acceptance Scenarios**:

1. **Given** a user has email enabled for the Workflow category, **When** a workflow of theirs fails, **Then** they receive exactly one email with an HTML body, a plain-text alternative, and a link to the failed run.
2. **Given** a security-category event (e.g., two-factor authentication disabled), **When** the email is sent, **Then** it contains only a generic "sign in to view" message and no sensitive specifics.
3. **Given** the mail service is temporarily unavailable, **When** delivery is attempted, **Then** it is retried with increasing delays and succeeds once the service recovers, without duplicate emails.
4. **Given** delivery fails with a permanent error (e.g., the recipient address is rejected), **When** the failure is recorded, **Then** it is not retried and is moved to the failed/dead-letter state with its reason.
5. **Given** a transient failure persists beyond the configured retry limit, **When** the final attempt fails, **Then** the delivery enters the failed/dead-letter state and remains available for administrator investigation.
6. **Given** a delivery worker restarts or two workers run concurrently, **When** pending emails are processed, **Then** no email is sent more than once.
7. **Given** notification content includes user-provided text (e.g., a document name containing markup or a line break), **When** the email is produced, **Then** that text is shown as plain text, cannot alter the email's structure, and cannot inject mail headers.
8. **Given** a user's in-app notification delivered successfully but email failed, **When** status is inspected, **Then** each channel shows its own independent delivery status.

---

### User Story 4 - Control which notifications I receive and how (Priority: P2)

A user opens Notification Preferences and, for each category (Security, Account, Agent, Workflow, Document, Knowledge Base, Memory, System), chooses whether to receive it in-app and/or by email. Mandatory categories (security-critical and required account notices) are shown as locked-on with an explanation. Changes take effect for subsequent notifications.

**Why this priority**: Without control, email becomes noise and users disengage or mark it as spam; but preferences only matter once there is more than one channel (US3).

**Independent Test**: Toggle channel/category combinations and verify that subsequent events are delivered only on enabled channels, that mandatory security notifications are delivered regardless, and that locked options cannot be changed through the UI or by direct requests.

**Acceptance Scenarios**:

1. **Given** a new user who has never changed preferences, **When** they open preferences, **Then** they see sensible defaults (in-app on for all categories; email on for Security, Account, approval requests, and failures; email off for routine completions).
2. **Given** a user disables email for the Document category, **When** a document finishes processing, **Then** they receive an in-app notification but no email.
3. **Given** a user disables the in-app channel for the Workflow category, **When** a workflow completes, **Then** no in-app notification is shown for it.
4. **Given** a Critical security notification, **When** it is raised for a user who disabled all optional email, **Then** it is still delivered by email and in-app, because security policy requires it.
5. **Given** a user attempts to disable a mandatory category via a direct request that bypasses the UI, **When** the request is processed, **Then** it is rejected and the setting is unchanged.

---

### User Story 5 - Act on approval requests securely (Priority: P2)

When an agent or workflow pauses for approval, the authorized approver is notified in-app and by email with a secure link to the approval screen in Ask Lucy. Opening the link requires signing in; authorization is re-checked at that moment; the approval decision itself is made on the approval screen owned by the agent/workflow system, never inside the notification or email. The notification's history remains auditable.

**Why this priority**: Approvals block work until a human acts, so fast, trustworthy delivery is high-value — but it depends on US1–US3 and on the existing agent/workflow approval systems.

**Independent Test**: Put a workflow and an agent into "awaiting approval", verify the approver (and only the approver) is notified on both channels with a link to the correct approval screen; open the link signed out, signed in as a different user, and signed in as the approver after approval rights were revoked, and verify access is denied in each wrong case.

**Acceptance Scenarios**:

1. **Given** a workflow step requires approval, **When** it pauses, **Then** the authorized approver receives a High-priority "approval required" notification in-app and by email linking to that approval screen.
2. **Given** an approval link, **When** it is opened by someone who is not signed in, **Then** they must sign in before seeing anything about the request.
3. **Given** an approver whose approval rights were revoked after the email was sent, **When** they open the link, **Then** authorization fails and no approval details are shown.
4. **Given** an approval notification, **When** its content is inspected, **Then** it contains no approve/reject action that bypasses the approval screen.
5. **Given** an approval request that was already resolved, **When** the approver opens the notification, **Then** they see the request's current (resolved) state rather than being prompted to act.

---

### User Story 6 - Administrators monitor delivery and recover failures (Priority: P3)

An administrator opens the Notification Dashboard to see volumes created/sent/failed, delivery latency, success and failure rates per channel, retry counts, queue backlog, and the health of each delivery channel and provider. They drill into failed/dead-lettered deliveries — seeing the notification, channel, failure reason, attempt count, last attempt, next retry and a safe provider response — and retry them individually or in bulk. Every administrative action is audited.

**Why this priority**: Operational visibility is required for production readiness and for "no silent failures", but end users get value from US1–US5 without it.

**Independent Test**: Force failures (mail service down, rejected recipient) and verify that the dashboard shows accurate counts and health, that failed items expose their details without secrets, that retry re-queues them and they succeed once the cause is fixed, and that each admin action produces an audit entry. Verify that a user without the Notifications View permission cannot reach any of it, and that one with View but not Manage can see it but cannot retry.

**Acceptance Scenarios**:

1. **Given** the mail service is unreachable, **When** an administrator views channel health, **Then** the email channel shows as unhealthy with the time of the last successful send.
2. **Given** a dead-lettered delivery, **When** an administrator retries it and the cause has been resolved, **Then** it is delivered once and its status becomes Sent.
3. **Given** any administrator retry, template change or configuration change, **When** it completes, **Then** an audit entry records who did what, when, and to which item.
4. **Given** a failed delivery, **When** an administrator views it, **Then** credentials, full email bodies of sensitive categories, and other secrets are not displayed.
5. **Given** a user whose role lacks the Notifications View permission, **When** they request any administrative notification view or action, **Then** access is denied; **Given** a user whose role has Notifications View but not Manage, **When** they open the notification admin screens, **Then** they can see them but every change (retry, template edit or publish, announcement, localization change) is not offered and a direct request is rejected server-side.

---

### User Story 7 - Administrators manage versioned notification templates (Priority: P3)

An administrator manages the templates used to produce each notification type on each channel (e.g., "Workflow Approval Requested" has an email template and an in-app template). They can view templates, create a new draft version, edit it, preview it with sample data, send a test email to themselves, publish it, and archive old versions. Published versions cannot be edited — changes always produce a new version. Templates use only a defined set of variables (e.g., `user_name`, `workflow_name`, `workflow_execution_id`, `agent_name`, `document_name`, `status`, `action_url`); unknown variables are rejected at save time and no template can execute code.

**Why this priority**: Shipped default templates make US1–US5 work without this; editing capability improves tone and branding afterwards.

**Independent Test**: Create a draft, preview it with sample data, send a test, publish it, confirm new notifications use it, attempt to edit the published version (rejected), create v2, publish, confirm v1 is retained and v2 is used; attempt to save templates with unknown variables or code/script constructs and verify rejection.

**Acceptance Scenarios**:

1. **Given** a published template version, **When** an administrator tries to modify it, **Then** the change is refused and they are offered to create a new draft version.
2. **Given** a draft referencing an undefined variable, **When** the administrator saves or publishes it, **Then** it is rejected with a message naming the unknown variable.
3. **Given** a template containing script or code-like constructs, **When** it is previewed or rendered, **Then** those constructs are displayed inertly or rejected — nothing is executed.
4. **Given** sample data containing markup, **When** the template is previewed, **Then** the markup is shown as text, not rendered.
5. **Given** a newly published version, **When** later notifications of that type are produced, **Then** they use the new version, and already-sent notifications still record which version produced them.

---

### User Story 8 - Switch to Arabic and get my notifications in Arabic (Priority: P3)

English is the main language of Ask Lucy and of every notification and email. An administrator can enable localization and choose which languages the platform supports (English always, plus Arabic in this feature). While localization is enabled, a user — including an administrator — can switch their language to any supported language; from then on their notifications, emails, notification center and notification preferences — and, for administrators, the entire admin area — are in that language, laid out right-to-left for Arabic. Brand and product names, English names, acronyms and abbreviations (e.g., OpenAI, Anthropic, AI, MCP, 2FA) always stay as they are. A user who has not switched — or anyone while localization is disabled — sees everything in English exactly as before.

**Why this priority**: Required for the platform's Arabic-speaking audience, but dependent on templates (US7) and delivery (US1, US3), and English-only operation is fully usable without it.

**Independent Test**: With localization disabled, verify every notification, email and notification screen (user and admin) is English and no language choice is offered. As an administrator, enable localization with English and Arabic supported; switch a user to Arabic, trigger one notification of every type, and verify Arabic content and right-to-left layout in the notification center, preferences and email clients; switch an administrator to Arabic and verify every admin screen (page shell and all sections, including the notification administration screens) is in Arabic, right-to-left, with every protected name and acronym left untranslated. Remove Arabic from the supported languages and verify Arabic-language users fall back to English.

**Acceptance Scenarios**:

1. **Given** localization is disabled, **When** any notification or email is produced for any user, **Then** it is in English, and no language choice is offered to users.
2. **Given** localization is enabled and a user has not changed language, **When** a notification is produced for them, **Then** it is in English.
3. **Given** localization is enabled with Arabic supported, **When** a user switches their language to Arabic, **Then** the notification center and notification preferences are shown in Arabic, right-to-left, and every subsequent notification and email to them is in Arabic.
4. **Given** an administrator enables localization, **When** they choose the supported languages, **Then** they can select from the languages the platform has content for (English and Arabic), English cannot be deselected, users are offered only the selected languages, and the change is audited.
5. **Given** an administrator has switched their language to Arabic, **When** they open any admin screen — the page shell (page title and subtitle, admin sidebar, header controls) and every section: Dashboard, Users, Roles, Role assignments, System agents, AI providers, Default models, AI capabilities, Agent policies, Workflow policies, MCP servers, Jobs, and the notification dashboard, delivery monitoring, template management and localization settings — **Then** all interface text on it (headings, labels, statistics captions, chart titles and legends, table headers, buttons, menus, tooltips, dialogs, form validation, empty/loading/error states and confirmation messages) is in Arabic and the layout is right-to-left, while template content being edited keeps its own language.
6. **Given** an admin screen in Arabic, **When** it shows brand or product names (Ask Lucy, OpenAI, Anthropic, Google Gemini, OpenRouter, Hangfire), acronyms or abbreviations (AI, MCP, 2FA, SMTP, API, URL, ID, OCR), model identifiers, or data entered by people (role names such as "Super User", agent names, user names, email addresses), **Then** they appear exactly as in English — untranslated and not transliterated — and display in correct reading order within the surrounding Arabic text (e.g., the "AI providers" label is translated except for "AI", which stays in Latin letters).
7. **Given** Arabic-language users exist, **When** an administrator removes Arabic from the supported languages, **Then** those users receive English from then on, and their Arabic choice is restored if Arabic is supported again.
8. **Given** a user switched to Arabic, **When** they switch back to English, **Then** subsequent notifications are in English; notifications already delivered keep the language they were produced in.
9. **Given** a user switched to Arabic and localization is later disabled, **When** notifications are produced, **Then** they are in English, and the user's Arabic choice is kept for when localization is re-enabled.
10. **Given** an Arabic template version is missing or not yet published for some type, **When** that notification is produced for an Arabic-language user, **Then** the English version is used and nothing is shown blank or broken.
11. **Given** Arabic content with embedded English names, identifiers or numbers (e.g., a workflow name), **When** displayed in the app or in email, **Then** text direction remains correct and readable.

---

### User Story 9 - Existing notifications and account emails move onto the hub without disruption (Priority: P2)

Ask Lucy already notifies users in three separate ways: document-processing notifications, AI-memory notifications (a memory was auto-created, auto-approved, or conflicts and needs confirmation), and the branded account emails (email confirmation, confirmation resend, new-email confirmation, password reset link, password-changed notice, account support request). All of them move onto the hub. Users keep every notification they already had, with its read state, in the one notification center; account emails keep their current branded design and arrive at least as quickly as today; and no event is ever notified twice during or after the switch.

**Why this priority**: Leaving the old mechanisms in place would give users two notification lists and duplicate notifications, and would leave the most security-sensitive emails outside the hub's reliability, retry, audit and monitoring guarantees. It depends on US1 and US3.

**Independent Test**: Before migration, record every existing document and memory notification with its read state and trigger every account email type. After migration, verify every prior notification appears once in the notification center with the same read state, message and link; the old separate notification lists no longer exist; every account email type is produced through the hub, looks the same as before, carries a working one-time link, and is visible in delivery monitoring; and each document, memory and account event yields exactly one notification.

**Acceptance Scenarios**:

1. **Given** a user had 12 document notifications (4 unread) and 3 memory notifications (1 unread), **When** the migration completes, **Then** their notification center shows those 15 notifications with 5 unread, each in its correct category, with its original time and link.
2. **Given** the migration has completed, **When** a document finishes processing or a memory conflict needs confirmation, **Then** exactly one notification is produced through the hub and none through the former mechanism.
3. **Given** a user requests a password reset, **When** the email arrives, **Then** it has the same branded design as before, contains a working one-time reset link, and its delivery appears in administrator monitoring without the link or token being visible there.
4. **Given** a person registering or changing their email address, **When** the confirmation email is sent, **Then** it goes to the address being confirmed (which is not yet verified), not to any other address.
5. **Given** an account email such as a password reset, **When** it is produced, **Then** no in-app copy is created, because the recipient may be unable to sign in.
6. **Given** a user sends an account support request, **When** it is sent, **Then** it reaches the configured support mailbox through the hub with the same content as before, and the requester's input cannot alter its headers.
7. **Given** a memory conflict notification, **When** the user follows its action, **Then** they reach the same memory-confirmation screen as before the migration.

---

### Edge Cases

- A notification's related item (workflow run, document) is deleted before the user opens the notification: the related action shows a "no longer available" message, never an error page or another user's content.
- The recipient deletes their account while deliveries are queued: pending deliveries are cancelled and no email is sent to the removed address.
- A user's email address is unverified or missing: email is skipped for non-mandatory categories and the delivery records why; in-app delivery proceeds. Account emails whose purpose is confirming that address are still sent to it (FR-009c).
- A one-time account link (password reset, email confirmation) expires while its email is waiting for a retry: the email is not sent with a dead link; the delivery is marked Expired, and the user can simply request a new one.
- A user switches language while notifications for them are queued: queued notifications are produced in the language in effect when they are rendered for sending, and the recorded language matches what was sent.
- Localization is disabled, or Arabic is removed from the supported languages, while a user has Arabic selected: they see English everywhere, and their Arabic choice returns when Arabic is supported again.
- An administrator tries to support a language the platform has no content for, or to deselect English: the choice is not offered or is rejected.
- A server-side error or validation message appears on an Arabic admin screen: the user sees Arabic text for it; technical details such as correlation IDs and error codes remain untranslated.
- A long Arabic label does not fit the space an English label occupied (sidebar item, statistic card, table header): it wraps or truncates with the full text available on hover and to screen readers, never overlapping other content.
- A migrated legacy notification refers to a document or memory that has since been deleted: it still displays its original message, and its action shows "no longer available".
- The same underlying event is raised twice (e.g., a retried workflow step): the user receives one notification, not two, when the event carries the same identity.
- A burst of notifications (e.g., a bulk upload of 500 documents): the notification center stays responsive, and the unread count stays correct.
- A critical announcement targets more users than the mail service accepts in one burst: every in-app copy appears at once, emails are spread over time within the sending limit, and each queued email still waiting when the announcement expires (e.g., the maintenance window has ended) is marked Expired and not sent.
- A notification expires before delivery: it is marked Expired and not delivered.
- The user is offline when a notification is created: it is shown next time they open the app; nothing is lost.
- A template variable's value is missing at render time: a safe placeholder or fallback text is used, the gap is logged, and no raw variable syntax is shown to the user.
- A link in notification content points outside Ask Lucy: it is removed or clearly marked as external unless explicitly authorized.
- The mail service accepts the message but the recipient's server later bounces it: the delivery shows Sent (handed off), not Delivered; bounce handling is outside this feature.
- Clock skew or a worker crash between "sent" and "recorded as sent": the design must prefer at-most-once for email over duplicates where exactly-once is impossible, and record the ambiguity for administrators.
- An administrator retries a dead-lettered delivery whose notification has since expired or been deleted: the retry is refused with an explanation.

## Requirements *(mandatory)*

### Functional Requirements

**Central hub & event sources**

- **FR-001**: The system MUST provide a single notification entry point that all platform modules use to request notifications; modules MUST NOT send email or deliver on any channel themselves.
- **FR-002**: A module requesting a notification MUST specify only what happened (notification type, recipient or recipient-resolution context, related item, variables, optional language), never the channel or provider.
- **FR-003**: The hub MUST decide delivery by evaluating recipient, notification type, category, priority, available channels, the recipient's preferences, mandatory-channel rules, template and language.
- **FR-004**: The system MUST generate notifications for: agent started, completed, failed and requires approval; workflow started, completed, failed, paused and requires approval; document upload completed, processing completed/failed, OCR completed/failed, version created, storage limit reached, indexing completed/failed; knowledge base indexing completed/failed and knowledge base updates made by someone other than the owner (defined now, emitted once such an actor exists); memory auto-created, auto-approved, and conflict needing confirmation; account and security events already raised by the authentication system (email confirmation and its resend, new-email confirmation, password reset link, password changed, two-factor authentication changes, account support request); and administrator-published system announcements (maintenance, service degradation, important announcements).
- **FR-004a**: Administrators MUST be able to publish a system announcement with a title, message, kind (maintenance, service degradation, important announcement), target audience (all active users, or users holding selected roles), optional end time, and a critical flag; publishing is audited. Every system announcement MUST be delivered in-app to its targeted active users. Email MUST be sent only for announcements the administrator explicitly marks as critical; it MUST go only to targeted users who have not turned off System email (System email is never mandatory), and it MUST be sent at a rate that stays within the mail service's sending limits, so a large recipient list is spread over time rather than sent as one burst. Announcements MUST NOT be usable as a mass marketing channel.
- **FR-004b**: Uploading a document to a knowledge base MUST start background retrieval indexing of that document, so the knowledge base indexing completed/failed notifications reflect real work. An indexing failure MUST be recorded for administrators and notified to the owner; it MUST NOT fail the upload itself.
- **FR-005**: The system MUST define Conversation and Billing categories and types so they can be emitted later, but MUST NOT emit billing notifications in this feature.
- **FR-006**: Notification requests arising from an important state change MUST be recorded durably together with that state change, so a crash or failed response after the change cannot lose the notification.
- **FR-007**: Delivery MUST happen asynchronously; a slow or failed delivery MUST NOT fail, delay or roll back the operation that raised the event.
- **FR-008**: The system MUST de-duplicate notification requests carrying the same event identity for the same recipient.
**Migration of existing notification sources**

- **FR-009**: All existing notification sources MUST be moved onto the hub in this feature: document-processing in-app notifications, AI-memory in-app notifications, and every account email (email confirmation and its resend, new-email confirmation, password reset link, password-changed notice, account support request). After release, none of them may notify through its former mechanism.
- **FR-009a**: Every existing document and memory notification MUST be carried into the hub with its recipient, category, type, message, related item, original creation time and read state preserved; the former separate notification lists and live-update channels MUST be retired in the same release, so users see one notification center and never receive the same event twice.
- **FR-009b**: Migrated account emails MUST keep their current branded design (spec 061), content and one-time links as the first published version of their templates, and MUST be delivered at least as quickly as before migration. After migration they are managed like any other template (FR-040).
- **FR-009c**: Account emails MUST be email-only (no in-app copy) and MUST be able to target an address that is not the user's verified account address where the flow requires it: the unverified address being confirmed, the new address in an email change, or the server-configured support mailbox (which MUST never be exposed to clients).
- **FR-009d**: One-time tokens and links in account emails MUST NOT be stored in readable form in notification records, delivery records, logs, metrics, audit entries or administrator views, and MUST be rendered only at the moment of sending.
- **FR-009e**: Migrated account-recovery flows MUST preserve their existing protection against revealing whether an address has an account: the requesting page's response and timing MUST NOT depend on whether a notification is produced.

**Notification record & lifecycle**

- **FR-010**: Each notification MUST record: identifier, recipient, category, type, title, message, priority (Low, Normal, High, Critical), status, created time, read time, expiry time, related item type and identifier, in-app action route, non-sensitive metadata, correlation identifier, and the template version used.
- **FR-011**: Notifications MUST follow the lifecycle Created → Queued → Processing → Sent → Delivered → Read, with Failed, Cancelled and Expired as terminal alternatives; invalid transitions MUST be rejected.
- **FR-012**: Delivery status MUST be tracked per channel independently of the notification's own status.
- **FR-013**: Notifications MUST NOT store sensitive data beyond what is needed to display them (no credentials, tokens, full document contents, or internal error traces).

**Notification center (in-app)**

- **FR-014**: Users MUST be able to view their notifications newest-first, grouped by date, with pages loaded on demand; the full history MUST never be loaded at once.
- **FR-015**: Users MUST be able to filter by category and by read/unread state.
- **FR-016**: Users MUST be able to view a notification's details, follow its related action, mark it read, mark all read, and delete it.
- **FR-016a**: Deleting a notification MUST hide it from its owner immediately and everywhere (list, details, unread count, live updates) without removing the record; the record MUST be removed permanently only by retention cleanup (FR-059). Notifications of every category, including approval and security, MUST be deletable by their owner; deletion MUST NOT remove or alter delivery records or the audit history (FR-037, FR-054). A deleted notification MUST NOT be restorable by the user.
- **FR-017**: The unread count MUST be shown on every page and stay consistent with the list.
- **FR-018**: New notifications MUST appear for online users without a page refresh, using the platform's existing real-time mechanism; no second real-time mechanism may be introduced.
- **FR-019**: The notification center MUST show distinct loading, empty and error states; every failed load or action MUST show visible feedback with a retry option.
- **FR-020**: The notification center MUST support light and dark themes, right-to-left layout, keyboard operation and screen readers, and work from mobile to desktop widths.

**Email channel**

- **FR-021**: The system MUST deliver email through the existing outgoing mail service over an encrypted connection upgraded with STARTTLS as required by the server's policy; certificate validation MUST NOT be disabled in production.
- **FR-022**: Each email MUST include a subject, an HTML body, and a plain-text alternative, and MAY include a reply-to address and authorized attachments; emails MUST be responsive, accessible, readable in light and dark mail clients, and MUST NOT rely on scripts.
- **FR-023**: The email provider MUST be replaceable (e.g., with a different mail provider) without changing how notifications are requested, routed or templated.
- **FR-024**: Mail-service credentials MUST stay server-side, MUST NEVER appear in the client application, logs, admin views or error messages, and MUST NOT be stored in plain text where avoidable.
- **FR-025**: For sensitive categories (security at minimum), emails MUST contain only a minimized message directing the user to sign in, not the sensitive details; minimization MUST be configurable per notification type.

**Reliability**

- **FR-026**: Failed deliveries MUST be retried per a configurable policy with increasing delay between attempts and a maximum attempt count; policy MAY vary by priority.
- **FR-027**: Permanent failures (e.g., invalid or rejected recipient) MUST NOT be retried; deliveries exceeding the retry limit MUST enter a failed/dead-letter state that is retained for investigation.
- **FR-028**: Each delivery MUST carry a unique delivery identity so that restarts, retries, network failures and concurrent processing do not produce duplicate emails.
- **FR-029**: Background delivery processing MUST be safe to stop and restart at any point without losing or duplicating deliveries, and MUST NOT let two workers process the same delivery simultaneously.
- **FR-030**: Every delivery failure MUST be recorded with its reason and correlation identifier and be visible to administrators; no failure may be silently discarded.

**Preferences**

- **FR-031**: Users MUST be able to enable or disable each channel (in-app, email) per category: Security, Account, Agent, Workflow, Document, Knowledge Base, Memory, System (Billing added when billing notifications exist).
- **FR-032**: Mandatory notifications (security-critical and policy-required account notices) MUST be delivered regardless of preferences; the preferences screen MUST show them as locked, and the server MUST reject attempts to disable them.
- **FR-033**: The preference model MUST accommodate a delivery frequency per category (Immediate, Daily digest, Weekly digest); in this feature only Immediate is offered.
- **FR-034**: Users without saved preferences MUST receive the documented defaults (see Assumptions).

**Approval notifications**

- **FR-035**: When an agent or workflow requires approval, the hub MUST notify the approver resolved by the owning approval system, with a link to that system's approval screen.
- **FR-036**: Approval notifications MUST NOT contain any mechanism to approve or reject; opening the link MUST require sign-in and MUST re-check authorization at that moment.
- **FR-037**: Approval notification history (created, delivered, read) MUST remain auditable.

**Templates**

- **FR-038**: Each notification type MUST be produced from a template per channel and language; templates MUST have name, category, channel, subject, body, declared variables, language, version and status (Draft, Published, Archived).
- **FR-039**: Published template versions MUST be immutable; edits MUST create a new version; each sent notification MUST record the version used.
- **FR-040**: Administrators MUST be able to view, create draft versions, edit drafts, preview with sample data, send a test to themselves, publish and archive template versions. This applies to every template, including the account and security email templates; they are governed by the same rules (FR-041, FR-042, FR-047) and the same permission (Manage notifications, FR-054a) as any other template.
- **FR-041**: Templates MUST use only declared variables from an approved set; unknown variables MUST be rejected at save/publish time.
- **FR-042**: Template rendering MUST be logic-free and safe: it MUST NOT execute code, scripts or queries of any kind, and variable values MUST be escaped for the output context (HTML, plain text, email subject).
- **FR-043**: The system MUST ship a published default template (email and in-app, English) for every notification type it emits.

**Localization**

- **FR-044**: English MUST be the application default language for the website and for all notifications and emails. While localization is disabled, every notification and email MUST be produced in English regardless of any other setting. While localization is enabled, the language MUST be chosen as: explicit notification language, then the user's chosen language, then the application default (English), considering only supported languages; when no published template exists in the chosen language, the English template MUST be used.
- **FR-044a**: Localization MUST be a platform setting controlled by holders of the Manage notifications permission (FR-054a), disabled by default. When enabling it, administrators MUST choose the supported languages from those the platform has content for (English and Arabic in this feature); English MUST always be supported and cannot be deselected. Every change MUST be audited and MUST take effect without a redeployment.
- **FR-044b**: While localization is enabled, users MUST be able to switch their own language among the supported languages; the choice MUST persist across sessions and devices and apply to subsequent notifications. While localization is disabled, or when the chosen language is no longer supported, the language choice MUST NOT be offered or applied (English is used), and the previously saved choice MUST be retained.
- **FR-044c**: Each notification MUST record the language it was produced in, and MUST keep that language when later viewed after the user changes language.
- **FR-045**: The system MUST support English and Arabic, including right-to-left layout in the notification center, notification preferences, the entire admin area and email, correct handling of mixed-direction text, and allow further languages to be added as content (then selectable by administrators) without changing notification logic.
- **FR-046**: This feature MUST ship complete Arabic content: a published Arabic version of every default template on every channel it uses (including migrated account emails); a complete Arabic notification center, notification details, notification preferences and language switch; and a complete Arabic admin area. All interface text on these screens MUST come from localizable resources, not literals.
- **FR-046a**: The Arabic admin area MUST cover the admin page shell (page title and subtitle, admin sidebar and its collapse control, and the header controls shown on admin pages) and every admin section: Dashboard, Users, Roles, Role assignments, System agents, AI providers, Default models, AI capabilities, Agent policies, Workflow policies, MCP servers, Jobs, and the notification dashboard, delivery monitoring, template management, system announcements and localization settings introduced by this feature. On each, all interface text MUST be translated: headings, labels, statistic and chart captions, chart legends, table headers, buttons, menus, tooltips, dialogs, form validation messages, empty/loading/error states, confirmations and toasts, and accessible names read by screen readers.
- **FR-046b**: Brand, product and company names (e.g., Ask Lucy, Flumeria, OpenAI, Anthropic, Google Gemini, OpenRouter, Autodesk, Hangfire), English proper names, acronyms and abbreviations (e.g., AI, MCP, 2FA, API, SMTP, STARTTLS, URL, ID, OCR, RAG, PDF), model identifiers, technical identifiers (IDs, error codes, correlation IDs), email addresses, URLs, and content entered by people (role names, agent names, user names, template variables) MUST NOT be translated or transliterated in any language, on any screen, notification or email. These protected terms MUST be kept in one maintained do-not-translate list used for all translation work.
- **FR-046c**: Admin sections and notification screens added after this feature MUST include Arabic text that follows FR-046b as part of their own delivery.

**Links, attachments & content security**

- **FR-047**: Notification links MUST point to trusted Ask Lucy routes; external links MUST NOT be generated unless explicitly authorized and MUST be clearly marked as external.
- **FR-048**: Links to files MUST use the platform's existing expiring, authorization-enforcing, scoped signed links and MUST NOT expose permanent file locations.
- **FR-049**: Attachments, where permitted, MUST be explicitly authorized through existing file authorization, MUST NOT expose sensitive documents via public links, and large files MUST NOT be loaded entirely into memory.
- **FR-050**: All user- or model-controlled values in notifications MUST be escaped for their output context to prevent HTML injection, cross-site scripting, malicious links and header injection.
- **FR-051**: The sender and return-path of emails MUST come only from server configuration; recipient, reply-to, subject and all header values MUST be validated, and line breaks or control characters MUST NOT reach email headers.

**Authorization & isolation**

- **FR-052**: All notification endpoints and views MUST require an authenticated user; users MUST access only their own notifications and preferences.
- **FR-053**: Authorization MUST be enforced before notification content is generated or delivered (a user must not be notified with content about items they cannot access).
- **FR-054**: Administrative views and actions MUST be recorded in an immutable audit trail (actor, action, target, time, outcome).
- **FR-054a**: The feature MUST add one "Notifications" area to the platform-defined permission catalogue (spec 055) with two permissions: **View notifications** (read-only access to the notification dashboard, delivery monitoring, failed deliveries, channel health, templates, system announcements and localization settings) and **Manage notifications** (retrying failed deliveries, creating, editing, publishing and archiving template versions, sending template tests, publishing system announcements, and changing localization settings). Every notification admin view and action MUST be enforced server-side against these permissions. As catalogue permissions, both are automatically held by the built-in Super User and Administrator roles and can be attached to custom roles.

**Administration & observability**

- **FR-055**: Administrators holding the relevant Notifications permission (FR-054a) MUST be able to view notification statistics, inspect and retry failed deliveries individually or in bulk, inspect channels and provider health, manage templates, and view delivery metrics.
- **FR-056**: Failed-delivery views MUST show notification, channel, failure reason, attempts, last attempt, next retry and a provider response only where it contains no sensitive data.
- **FR-057**: The system MUST record metrics for notifications created, sent and failed, delivery latency, email success and failure rates, retry count, backlog depth, unread notifications, and provider errors, with correlation identifiers linking each notification to the event that caused it.
- **FR-058**: Health checks MUST report mail-service connectivity, delivery-worker liveness, backlog processing, and database connectivity, and be extensible to future providers.

**Retention**

- **FR-059**: Retention MUST be configurable separately for read notifications, user-deleted notifications, failed notifications, delivery records and audit records; expired data MUST be cleaned up automatically in the background; audit and security retention MUST follow project policy.

**Extensibility**

- **FR-060**: Adding a new channel (WhatsApp, Teams, Slack, SMS, push, webhooks) or a new provider for an existing channel MUST require only adding that channel/provider and its templates, with no change to how modules request notifications or how routing works.

### Key Entities

- **Notification**: One message intended for one recipient about one event — category, type, title, message, priority, status, lifecycle times, related item, action route, metadata, correlation identity, template version, and the time the owner deleted it (hidden from the owner once set; removed later by retention cleanup). Owned by exactly one user.
- **Notification Delivery**: One attempt stream to deliver a notification on one channel — channel, status, unique delivery identity, attempt count, last/next attempt, failure reason, safe provider response. A notification has one delivery per selected channel.
- **Notification Template**: A named, per-type, per-channel, per-language message definition.
- **Notification Template Version**: An immutable (once published) revision of a template with subject, body, declared variables and status.
- **Notification Preference**: A user's per-category, per-channel enabled flag and frequency.
- **Notification Channel**: A delivery medium (in-app, email; later WhatsApp, Teams, …) with its enabled state and health.
- **Notification Event**: The durable record of "something happened that may require notifying someone", captured with the originating state change (outbox entry).
- **Notification Failure**: The record of a failed or dead-lettered delivery with its reason and attempts (may be represented as the failed state of a delivery).
- **Notification Audit Entry**: Immutable record of administrative actions and approval-notification history.
- **System Announcement**: An administrator-published message — kind, title, message, target audience, optional end time (after which undelivered copies expire), critical flag (which alone enables email), publisher and publish time. Fans out to one Notification per targeted user.
- **Localization Setting**: Platform-wide enabled flag (disabled by default) and the set of supported languages (always including English); administrator-controlled and audited.
- **User Language Choice**: A user's selected language (one of the supported languages), stored with the existing user profile rather than as a duplicate user record.
- **User** (existing): The platform's existing user identity; not duplicated.
- **Legacy document and memory notifications** (existing): Source of migrated notifications; retired after migration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For online users, 95% of new in-app notifications appear within 5 seconds of the triggering event, without a page refresh.
- **SC-002**: 95% of emails are handed to the mail service within 2 minutes of the triggering event when the mail service is healthy.
- **SC-003**: In fault-injection testing (worker restart mid-delivery, mail-service outage, concurrent workers, request crash after the event), 0 notifications are lost and 0 duplicate emails are sent across at least 1,000 test deliveries.
- **SC-004**: The unread count and the first page of the notification center load in under 1 second for a user with 100,000 historical notifications.
- **SC-005**: The system sustains at least 10,000 notifications per hour with the in-app delivery backlog returning to empty within 10 minutes after the burst ends. The email backlog drains at the configured mail sending limit (for example, 500 emails at 40 per minute take about 12.5 minutes), and mandatory emails are never held behind optional ones.
- **SC-006**: 100% of emitted notification types have a published English and a published Arabic template on every channel they use.
- **SC-007**: 0 findings in security tests covering cross-user access, HTML/script injection, email header injection, template injection, unauthorized signed-link access, and sensitive-data exposure in emails, logs and admin views.
- **SC-008**: 100% of delivery failures are visible to administrators with a reason; 100% of administrative actions produce an audit entry.
- **SC-009**: Mandatory security notifications are delivered in 100% of test cases, including for users who disabled every optional channel.
- **SC-010**: A new user can find and change a notification preference in under 30 seconds on first attempt.
- **SC-011**: Notification center, preferences and every admin screen pass automated accessibility checks with no serious or critical violations, in light, dark, and right-to-left (Arabic) modes.
- **SC-012**: Adding a stub test channel in a proof exercise requires no changes to any module that requests notifications or to routing rules.
- **SC-013**: After migration, 100% of pre-existing document and memory notifications are present exactly once in the notification center with their original read state, and 0 events are notified through a former mechanism.
- **SC-014**: 95% of password-reset and email-confirmation emails are handed to the mail service within 1 minute of the request, no slower than before migration.
- **SC-015**: With localization disabled, 100% of notifications, emails and notification screens are in English; with localization enabled and Arabic supported, 100% of notifications for Arabic-language users are in Arabic whenever an Arabic template is published, and 100% of interface text on the user notification screens and on every admin screen is in Arabic for Arabic-language users.
- **SC-016**: 0 protected terms (brand/product names, acronyms, abbreviations, identifiers, user-entered names) are translated or transliterated across all Arabic screens, notifications and emails, verified against the do-not-translate list.

## Assumptions

- **Recipients & isolation**: The platform isolates data per user; "tenant isolation" means per-user ownership until an organization/tenant concept exists, at which point notifications inherit it.
- **Existing capabilities reused**: the existing identity and roles (administrator role), existing authorization, audit, observability and correlation-id infrastructure, existing real-time push mechanism, existing background job processing (already used for account emails), existing signed file links, and the existing outgoing-mail abstraction and its myasp.net STARTTLS configuration.
- **Existing mail abstraction**: the current outgoing-mail abstraction (recipient, subject, HTML body, plain-text body) is extended rather than duplicated to add optional reply-to and attachments.
- **Default preferences**: in-app on for every category; email on for Security, Account, System (which emails only administrator-marked critical announcements, FR-004a), anything requiring approval, and any failure; email off for routine "started"/"completed" events and for Memory (which was in-app only before migration). Security and required account notices are mandatory.
- **Preference precedence**: a user's saved choice for a category and channel applies to every optional type in that category that can use that channel. Without a saved choice, each type uses its own default. A saved choice never affects mandatory notices and never adds a channel a type does not support.
- **Existing knowledge-base documents**: documents uploaded before this feature are not indexed retroactively; they are indexed when re-uploaded.
- **Language**: English is the main language of the website and of every notification and email. Localization starts disabled; Arabic is used only when an administrator has enabled localization with Arabic among the supported languages and a user has switched to Arabic. Users have no stored language choice today, so every existing user starts on English.
- **Arabic scope**: "Full Arabic" covers notification content, the user-facing notification screens (notification center, details, preferences, language switch), and the entire admin area (FR-046a). The rest of the application (chat, workspace, studio, knowledge bases, landing and sign-in pages, and so on) remains English in this feature.
- **Jobs**: the Jobs entry opens the third-party background-job dashboard in a separate tab. Its entry label and anything Ask Lucy shows around it are translated; the third-party dashboard's own interface is not controlled by Ask Lucy and stays English.
- **Numbers and dates in Arabic**: numbers use Western digits (0–9) and dates use the Gregorian calendar with Arabic month names. This keeps statistics, identifiers and model versions consistent with the untranslated terms around them.
- **Charts in right-to-left**: page layout mirrors for Arabic, but time-based chart axes keep time running left-to-right so trends read the same in both languages.
- **Arabic translations**: Arabic template and screen text is supplied as part of this feature and reviewed by an Arabic speaker before release.
- **Priority defaults**: approvals and failures High; security events Critical; completions Normal; "started" events Low. Priority influences default channels, retry policy, and visual prominence.
- **Retry defaults**: up to 5 attempts with exponential backoff over roughly one hour; Critical notifications may use a more aggressive schedule.
- **Retention defaults**: read notifications 90 days; user-deleted notifications 30 days after deletion; failed/dead-letter records 30 days after final failure; delivery records 90 days; audit records per project policy (not automatically deleted by this feature). All configurable.
- **Notification expiry**: notifications without an explicit expiry do not expire; system announcements expire when their maintenance window ends.
- **Login alerts**: covered only by account/security events the authentication system already raises; new-device or anomalous-login detection is not built by this feature.
- **Conversation notifications**: categories and types are defined; no existing conversation operation is long-running enough to emit them, so none are emitted yet.
- **"Delivered" meaning**: for email, the system can confirm hand-off to the mail service ("Sent"), not inbox arrival; bounce processing is out of scope.
- **Digests**: frequency is stored but only Immediate is offered; daily/weekly digest delivery is a follow-up.
- **Branding**: email templates reuse the branded design established by spec 061; the migrated account emails become the first published versions of their templates.

## Dependencies

- Agent runtime and agent approval system (SPEC-008 / specs 045, 057) — emit agent events; own approver resolution and approval screens.
- Workflow orchestration engine and workflow approval system (SPEC-010 / spec 022) — emit workflow events; own approver resolution and approval screens.
- Document processing and RAG / knowledge base indexing (SPEC-005 / spec 016) — emit document and indexing events.
- Authentication and account management (spec 000, 058, 059) — emit account and security events; user preferred language.
- AI Memory (SPEC-006 / spec 018) — emits memory events; its existing memory notifications and confirmation screen are migrated (FR-009).
- Document processing — its existing document notifications are migrated (FR-009).
- Branded email templates (spec 061) and password recovery (spec 058) — the account emails, their design and their one-time link and anti-enumeration behaviour are migrated (FR-009b–FR-009e).
- Role management and permission catalogue (spec 055) — gains the Notifications area with View and Manage permissions (FR-054a).
- User profile / settings — gains the user's language choice.
- File storage signed links — for file links in notifications.
- Existing myasp.net outgoing mail service and its STARTTLS configuration.

## Risks

- **Duplicate or lost email under failure**: exactly-once email is impossible over standard mail protocols; mitigated by unique delivery identity, single-worker claiming, and preferring at-most-once with ambiguity flagged to administrators.
- **Mail-service limits**: the shared mail host may throttle or blacklist bursts (e.g., bulk document uploads); mitigated by batching, rate-limited sending, and conservative email defaults for routine events.
- **Notification fatigue**: too many notifications reduce engagement and cause spam reports; mitigated by defaults, preferences, and future digests.
- **Double or lost notifications during migration**: if old and new emitters overlap or neither is active during the switch, events could be notified twice or not at all; mitigated by switching emitters, migrating data and retiring the old lists in one release (FR-009a).
- **Regressing account emails**: password reset and email confirmation are security-critical and time-sensitive; moving them behind a queue could slow them, leak one-time tokens into stored records, or weaken the existing anti-enumeration behaviour. Mitigated by FR-009b–FR-009e, SC-014, and prioritising account emails in delivery.
- **Shared-host outbound timeouts**: the hosting provider has a history of too-short outbound timeouts producing false "unavailable" signals; health checks and retry timeouts must tolerate this.
- **Template misuse by administrators**: poorly written templates could leak data or mislead users; mitigated by declared-variable allow-list, logic-free rendering, preview, and audit.
- **Localization groundwork**: the application has no multilingual interface today, so this feature introduces the first localizable screens, the language switch and the localization setting; the scope covers the user notification screens and the entire admin area (twelve existing sections plus the new notification admin screens), which retrofits existing, tested admin pages. This is a large share of the feature's effort, and the approach will set the precedent for translating the rest of the app. The plan should consider delivering admin localization as its own independently shippable slice.
- **Over-translation**: translators or tooling may translate protected terms (e.g., provider or model names), confusing administrators and breaking recognition; mitigated by the do-not-translate list (FR-046b) and SC-016 verification.
- **Right-to-left regressions in existing admin pages**: tables, charts, dialogs and icons in existing pages were built left-to-right only and may mis-align when mirrored; mitigated by right-to-left visual and accessibility checks on every admin section.
- **Translation quality**: machine-quality Arabic in security or account emails would erode trust; mitigated by native-speaker review before release.

## Security Threats

| Threat | Mitigation (requirement) |
|--------|--------------------------|
| Cross-user reading or manipulation of notifications (ID guessing) | Ownership enforced on every read/write (FR-052) |
| Notifying about items the recipient cannot access (information disclosure) | Authorization before content generation (FR-053) |
| XSS / HTML injection via document names, agent output, workflow names | Context-aware escaping (FR-042, FR-050) |
| Email header / SMTP header injection | Server-configured sender, header validation, no control characters (FR-051) |
| Template injection / code execution | Logic-free rendering, declared variables only (FR-041, FR-042) |
| Phishing via notification links | Trusted routes only, external links marked (FR-047) |
| Administrator (or hijacked administrator account) editing an account/security email template to phish users or strip its one-time link | Accepted risk (clarified): declared variables only (FR-041), trusted links only (FR-047), immutable versions and audit trail for every publish (FR-039, FR-054); no additional lock |
| Approval bypass via email links | No actions in notifications, sign-in and re-authorization required (FR-036) |
| Leaking sensitive details via email | Content minimization for sensitive categories (FR-025) |
| Credential exposure | Server-side only, never logged or displayed (FR-024) |
| Public exposure of protected files | Expiring, scoped signed links (FR-048, FR-049) |
| Disabling security alerts to hide account takeover | Mandatory categories enforced server-side (FR-032) |
| Unaudited administrative tampering | Immutable audit trail (FR-054) |
| Over-broad admin access (one Manage permission covers retries, templates, announcements and localization) | Accepted trade-off (clarified): single Notifications View/Manage pair (FR-054a); grant Manage only to roles trusted with account-email template content |
| One-time reset/confirmation tokens leaking via stored records, logs or admin views | Tokens never stored readably; rendered only at send time (FR-009d) |
| Account enumeration through migrated recovery flows | Response and timing independent of whether a notification is produced (FR-009e) |
| Support mailbox address harvesting | Address is server configuration, never exposed to clients (FR-009c) |

## Out of Scope

- WhatsApp, Microsoft Teams, Slack, SMS, mobile push and webhook channels (architecture-ready only).
- Communication marketplace; marketing, mass or campaign email; email marketing automation.
- Daily/weekly digest delivery and advanced scheduling.
- Billing notifications beyond defining their category and types.
- Email bounce/complaint processing and open/click tracking.
- New-device or anomalous-login detection.
- Approve/reject decision logic (owned by agent and workflow approval systems).
- Translating the rest of the application outside the admin area and the notification screens into Arabic; translating the third-party background-job dashboard opened from Jobs; content for languages other than English and Arabic (the supported-languages choice is ready for them).

## Migration Considerations

- New persistent data (notifications, deliveries, templates and versions, preferences, channels, outbox events, audit entries, the localization setting, and the user's language choice) is added alongside existing data.
- Default English and Arabic templates for every emitted type are seeded as published version 1 on deployment; the account email templates are seeded from the current branded emails so their appearance is unchanged.
- Existing document and memory notifications are copied into the hub with recipient, type, message, related item, original time and read state preserved; the copy must be repeatable without creating duplicates, and its counts verified against the source before the old data is retired.
- Old emitters are switched to the hub, the old notification lists and live-update channels are removed from the interface, and the data copy runs, all in one release, so there is no window of double or missing notification.
- The legacy document and memory notification data is retired in two steps per the constitution: stop reading and writing in this release, drop it in a later release once the migration is confirmed in production.
- Existing account email background jobs are replaced by hub deliveries; any account emails still queued in the old mechanism at deployment must drain or be re-queued through the hub, not lost.
- Users receive default preferences and English without a data backfill (defaults apply when nothing is saved); localization is deployed disabled, so behaviour is unchanged until an administrator enables it.
- Outgoing mail configuration and credentials remain in the existing server-side configuration (including the support mailbox address); no new secret is introduced, and production configuration (hand-deployed) must be checked before release.
