# Feature Specification: AI Memory System

**Feature Branch**: `018-ai-memory-system`

**Created**: 2026-08-09

**Status**: Draft

**Input**: User description: "Introduce a scalable AI memory architecture that enables Ask Lucy to remember user preferences, important information, project context, and long-term interactions. The memory system must provide personalized AI experiences while maintaining user control, privacy, transparency, and security. The system must be independent from LLM providers, vector databases, RAG implementation, and conversation storage. Memory should enhance AI interactions but never replace knowledge retrieval. Supports user, personal, project, conversation, and (future) knowledge memory types, a full memory lifecycle with configurable approval, ranking/retrieval/injection into prompts, conflict resolution, a Memory Center management UI, privacy controls, and provider-independent storage."

## Clarifications

### Session 2026-08-09

- Q: If the memory subsystem is unavailable or degraded at the exact moment Lucy is generating a response, what should happen to that chat turn? → A: Degrade gracefully — proceed without memory context; the failure is logged/observable to the team but never blocks or delays the user's response.
- Q: When an ambiguous memory conflict requires user confirmation (FR-016), should this interrupt the live chat turn or happen asynchronously? → A: Asynchronous — the conversation continues normally; the ambiguous memory is flagged "needs confirmation" and resolved later via the Memory Center, never blocking the live turn.
- Q: When background passive analysis of a conversation fails to complete (transient service error, timeout), what should happen? → A: Retry automatically with backoff; if retries are exhausted, log the failure for the operating team — not surfaced to the user, since this is best-effort background work rather than a user-initiated action.
- Q: Does memory storage/usage count against a user's subscription tier (Free/Professional/Enterprise) limits? → A: Memory is unmetered — available uniformly across all subscription tiers in this release; storage/usage tracking for billing is out of scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lucy remembers me across conversations (Priority: P1)

A user tells Lucy something about themselves or their work during a conversation ("I prefer TypeScript over JavaScript", "I work on BIM coordination for a mechanical contractor"). In a later, unrelated conversation, Lucy already knows this without the user repeating it, and responses are noticeably more relevant and personalized.

**Why this priority**: This is the core value proposition of the feature — without it, "memory" is just a settings page with no effect on the product experience. Every other capability exists to make this trustworthy and controllable.

**Independent Test**: Can be fully tested by having a user state a preference or fact in one conversation, starting a brand-new conversation later, asking a question where that fact is relevant, and confirming Lucy's response reflects it without the user restating it.

**Acceptance Scenarios**:

1. **Given** a user has previously told Lucy a stable preference (e.g., preferred response style, preferred AI model, a fact about their work), **When** they start a new, unrelated conversation and ask a question where that preference is relevant, **Then** Lucy's response reflects the remembered preference without the user restating it.
2. **Given** a user has disabled memory, **When** they state a preference and later start a new conversation, **Then** Lucy does not reference anything from the disabled period.
3. **Given** two remembered facts are relevant to the current message but only one fits within the response's context budget, **When** Lucy generates a response, **Then** the more important/relevant fact is used and the omission does not produce an incorrect or contradictory answer.

---

### User Story 2 - User reviews and manages what Lucy remembers (Priority: P1)

A user opens a "Memory Center" to see every piece of information Lucy currently remembers about them, organized by category, with a plain-language explanation of where each memory came from. They edit a memory that's now wrong, delete one they don't want kept, and search for a specific memory.

**Why this priority**: Transparency and control are prerequisites for user trust in an "always-on" memory system; shipping User Story 1 without this would let Lucy silently accumulate facts the user can't see or correct, which violates this platform's privacy and no-silent-behavior commitments. This must ship in the same increment as User Story 1, not after it.

**Independent Test**: Can be fully tested by generating at least one memory, opening the Memory Center, and confirming the user can view, search, edit, and delete it, with the change taking effect in subsequent conversations.

**Acceptance Scenarios**:

1. **Given** the user has one or more stored memories, **When** they open the Memory Center, **Then** they see each memory's content, category, source (which conversation or action created it), and creation date.
2. **Given** a stored memory is incorrect or outdated, **When** the user edits it, **Then** future conversations use the corrected version and the prior version is retained in that memory's history.
3. **Given** a stored memory, **When** the user deletes it, **Then** it is no longer used in any future conversation and no longer appears in the Memory Center.
4. **Given** many stored memories, **When** the user searches or filters by category, **Then** only matching memories are shown.

---

### User Story 3 - User approves what Lucy is allowed to remember (Priority: P2)

Lucy notices something in a conversation that looks worth remembering (a stated preference, a recurring fact) and, depending on the user's approval setting, either stores it automatically or holds it as a pending candidate that the user must explicitly approve or reject before it affects future conversations.

**Why this priority**: This governs how memories enter the system in the first place. It depends on User Stories 1 and 2 already existing (there must be memories to approve, and a place to review them) but is separable — a user can get value from explicit, self-declared memories (User Story 1) before candidate-detection-and-approval ships.

**Independent Test**: Can be fully tested by setting approval mode to manual, having a candidate memory generated, confirming it does not affect any conversation until approved, then approving it and confirming it now does.

**Acceptance Scenarios**:

1. **Given** the user's approval mode is "manual", **When** a new memory candidate is detected, **Then** it appears in the Memory Center as pending and is not used in any conversation until approved.
2. **Given** a pending memory candidate, **When** the user approves it, **Then** it becomes active and is eligible for use in future conversations.
3. **Given** a pending memory candidate, **When** the user rejects it, **Then** it is discarded and never used.
4. **Given** the user's approval mode is "automatic", **When** a new memory candidate is detected, **Then** it becomes active without requiring manual approval, and still appears in the Memory Center with its source disclosed.
5. **Given** the user's approval mode is "disabled", **When** a conversation occurs, **Then** no new memory candidates are created at all.

---

### User Story 4 - User controls memory privacy at the account level (Priority: P2)

A user who is uncomfortable with any part of the memory system can turn it off entirely, clear everything Lucy has ever remembered about them, export their memories to a file, or restrict which categories of memory are allowed — all from one place, with immediate effect.

**Why this priority**: Account-level privacy controls are a compliance and trust requirement independent of the day-to-day approve/reject workflow in User Story 3; a user must be able to "opt out entirely" even if they never touch per-memory approval.

**Independent Test**: Can be fully tested by enabling memory, generating memories, then disabling memory / clearing all memories / exporting memories, and confirming each action takes full effect (no further memory use, all memories removed, or a downloadable export produced, respectively).

**Acceptance Scenarios**:

1. **Given** memory is enabled, **When** the user disables it, **Then** Lucy stops creating new memories and stops using existing ones in conversations, without deleting the stored data.
2. **Given** stored memories exist, **When** the user chooses "clear all memories", **Then** every memory is permanently deleted after the user confirms the action.
3. **Given** stored memories exist, **When** the user requests an export, **Then** they receive a complete, human-readable copy of everything currently remembered about them.
4. **Given** the user restricts a specific memory category (e.g., disables "Project Memory"), **When** future conversations occur, **Then** no memories of that category are created or used, while other enabled categories continue to work.

---

### User Story 5 - User groups related work into a Project so memory stays scoped (Priority: P3)

A user creates a named Project (similar to Projects in other AI products) to group a set of related conversations — for example, everything to do with one client engagement or one BIM coordination effort. Facts and preferences that only make sense within that Project stay scoped to it and don't leak into unrelated conversations, while general preferences (language, response style, etc.) still apply everywhere.

**Why this priority**: Project scoping refines *where* a memory applies; it depends on the core remember-and-recall loop (User Stories 1-3) already existing, and a user gets value from the feature before Projects exist by relying on general (unscoped) memory.

**Independent Test**: Can be fully tested by creating a Project, stating a project-specific fact inside it, then confirming that fact is used in other conversations inside that same Project but not in unrelated conversations or other Projects.

**Acceptance Scenarios**:

1. **Given** a user creates a Project and states a fact relevant only to that Project, **When** they have another conversation within the same Project, **Then** the fact is available to Lucy; **When** they have a conversation outside that Project, **Then** the fact is not used.
2. **Given** a conversation is not associated with any Project, **When** the user asks Lucy something, **Then** only general (non-project-scoped) memories are considered.
3. **Given** a Project with project-scoped memories, **When** the user deletes the Project, **Then** those memories are archived (not immediately deleted) and remain visible and exportable from the Memory Center outside the Project context.

---

### User Story 6 - Lucy resolves contradictory memories (Priority: P3)

The user previously told Lucy "I use Angular" and later says "I moved to React." Instead of holding both facts and confusing future responses, Lucy detects the contradiction, updates the memory, and keeps a record that the old fact was superseded.

**Why this priority**: This is a quality/correctness refinement on top of the core remember-and-recall loop (User Stories 1-3). The system is useful without it, but memory quality degrades over time as users' facts and preferences change without it.

**Independent Test**: Can be fully tested by storing a fact, later stating a contradicting fact, and confirming the system flags/updates the memory rather than retaining both as equally valid, with the change visible in that memory's history.

**Acceptance Scenarios**:

1. **Given** an active memory states a fact, **When** the user states new information that directly contradicts it, **Then** the system detects the conflict and updates the memory to the newer information, retaining the prior value in history.
2. **Given** a detected conflict is ambiguous (the system cannot confidently tell whether the new statement supersedes or merely supplements the old one), **When** the conflict is detected, **Then** the current conversation turn continues normally without interruption, the memory is flagged "needs confirmation" and excluded from use until resolved, and the user confirms which version to keep asynchronously via the Memory Center.
3. **Given** a memory's history, **When** the user views that memory in the Memory Center, **Then** they can see prior versions and when each change occurred.

---

### Edge Cases

- What happens when a memory that is actively referenced mid-conversation is deleted by the user in the Memory Center at the same time? The current response completes using context already assembled; the memory is not used again afterward.
- What happens when the number of relevant memories for a conversation exceeds what can safely be included without crowding out the conversation itself? The system selects the most important/relevant subset and omits the rest, never truncating or corrupting the ones it does include.
- What happens when two memories in the same category say slightly different but not strictly contradictory things (e.g., two separate project facts)? Both are retained as distinct memories rather than one overwriting the other.
- What happens when a user with memory disabled explicitly asks Lucy to "remember" something? The explicit, in-the-moment request is honored as an intentional exception, and the user is told memory is otherwise off.
- What happens when a user deletes their account? All memories associated with that account are permanently deleted as part of account deletion.
- What happens when an exported memory file is requested for an account with zero memories? The user receives a valid, empty export rather than an error.
- What happens when the same fact is stated identically many times across many conversations? The system recognizes the duplicate and reinforces/updates the existing memory's recency and frequency rather than creating repeated near-identical entries.
- How does the system handle a candidate memory that appears to contain sensitive personal information (health, financial, legal, or similarly sensitive category)? It is always held for manual review regardless of the user's global approval mode, and is clearly labeled as sensitive in the Memory Center.
- What happens when passive background analysis and automatic approval combine to create a memory the user disagrees with before they ever notice it? The visible-signal requirement (FR-006a) ensures the user is informed close to when it happens, and the memory remains fully editable/deletable at any time from the Memory Center (User Story 2) — automatic mode never removes that control.
- What happens to a conversation's memory scoping if a conversation is moved into a Project after memories were already created from it? Memories already created from that conversation remain scoped as they were (general); only content analyzed after the move is considered for Project scoping, to avoid retroactively reclassifying history.
- What happens when the memory subsystem is unavailable or too slow at the moment Lucy generates a response? The response proceeds without memory context (graceful degradation per FR-014a) rather than blocking, delaying, or failing the turn; the failure is recorded for the operating team.

## Requirements *(mandatory)*

### Functional Requirements

**Memory types & scope**

- **FR-001**: System MUST support distinct categories of memory: user preferences (language, AI model, response style, coding/communication preferences, UI preferences), personal facts the user has provided, project-related context, and context extracted from past conversations.
- **FR-002**: System MUST allow each memory to be tied to the scope it applies to — either always relevant ("general") or relevant only within a specific Project — so that context from one area does not inappropriately leak into an unrelated one.
- **FR-002a**: System MUST allow users to create, name, rename, and delete Projects — workspaces that group a set of related conversations — modeled on the "Projects" concept found in comparable AI products (e.g., a named container a user creates for a client engagement or initiative). A conversation MAY belong to at most one Project at a time.
- **FR-002b**: Introducing Projects is scoped to this feature only as far as needed to support Project Memory: a name, an owner, and its member conversations. Additional Project-workspace capabilities beyond memory scoping (e.g., project-level file libraries, project-level custom instructions, project sharing/collaboration) are out of scope for this specification and, if desired, belong to a separate feature.
- **FR-003**: System MUST keep memory conceptually and technically independent of chat/conversation history storage, retrieval-augmented generation, and any specific AI provider or vector database — memory must continue to function if any of those are replaced.
- **FR-004**: System MUST NOT use memory as a substitute for knowledge retrieval — factual lookups against documents/knowledge bases remain the responsibility of retrieval, not memory.

**Memory lifecycle & creation**

- **FR-005**: System MUST support a memory lifecycle with distinct states: detected/candidate, pending review, approved/active, updated, archived, and deleted, and MUST make the current state of every memory visible to the user.
- **FR-006**: System MUST be able to create memory candidates both from explicit user statements ("remember that...") and from automatic, passive analysis of the user's conversation content — including conversations the user is not currently active in — without requiring the user to be present or to confirm each candidate in the moment.
- **FR-006a**: System MUST surface a visible, non-intrusive signal when a memory is created or activated via passive analysis or automatic approval (e.g., a notice the user can see in or near the Memory Center), so that memory accumulation is never silent even when no manual approval step occurred.
- **FR-006b**: System MUST retry a failed passive-analysis pass (transient service error, timeout) with backoff; if retries are exhausted, the failure MUST be logged to the operating team's observability trail rather than silently dropped. A failed analysis pass is best-effort background work and MUST NOT surface a user-facing error for that pass.
- **FR-007**: System MUST support three user-configurable memory approval modes — automatic (candidates become active without review), manual (candidates require explicit user approval before becoming active), and disabled (no new candidates are created) — applied per memory category. New accounts default to automatic mode for all categories, and users may change any category to manual or disabled at any time.
- **FR-008**: System MUST always require manual approval for a candidate memory the system identifies as containing sensitive personal information (e.g., health, financial, legal, or similarly sensitive content), regardless of the user's configured approval mode for that category.
- **FR-009**: System MUST retain a version history for every memory, capturing what changed, when, and what triggered the change, and MUST make that history visible to the user for that memory.

**Ranking, retrieval & use in conversation**

- **FR-010**: System MUST assign each memory attributes usable for ranking, including importance, confidence, recency, frequency of reinforcement, source, and optional expiration.
- **FR-011**: System MUST select which memories are relevant to a given conversation turn based on the current conversation, the user, and (when applicable) the active Project, before generating a response.
- **FR-012**: System MUST limit how much remembered content is used in any single response so that it never crowds out the user's actual conversation content, prioritizing the most important/relevant memories when more candidates exist than can be used.
- **FR-013**: System MUST prevent two conflicting memories from both being presented as equally valid within the same response.
- **FR-014**: System MUST make it possible for a user to understand, for any given response, why Lucy appears to know a particular fact (i.e., trace it back to the source memory).
- **FR-014a**: System MUST degrade gracefully when memory selection/retrieval is unavailable or fails at response-generation time: the conversation turn MUST proceed without memory context rather than blocking, delaying, or failing the response, and the failure MUST be logged/observable to the operating team rather than silently dropped.

**Conflict detection**

- **FR-015**: System MUST detect when newly stated information directly contradicts an existing active memory and update the memory to the newer information while preserving the prior value in history.
- **FR-016**: System MUST ask the user to confirm which version to keep when a detected conflict is ambiguous rather than automatically discarding either version. This confirmation MUST happen asynchronously via the Memory Center — the live conversation turn that surfaced the conflict MUST continue without interruption, and the ambiguous memory MUST be excluded from use in any conversation until the user resolves it.

**Memory Center (management)**

- **FR-017**: Users MUST be able to view a complete list of their memories, organized by category, each showing its content, category, source, creation date, and current lifecycle state.
- **FR-018**: Users MUST be able to search and filter their memories.
- **FR-019**: Users MUST be able to edit the content of an existing memory, with the edit recorded in that memory's history.
- **FR-020**: Users MUST be able to delete an individual memory, after which it is immediately excluded from all future conversations.
- **FR-021**: Users MUST be able to approve or reject any memory in a pending state.

**Privacy & account-level controls**

- **FR-022**: Users MUST be able to enable or disable memory entirely for their account, with disabling taking effect immediately for future conversations (existing stored memories are retained but not used) and not deleting existing data.
- **FR-023**: Users MUST be able to permanently delete all of their memories in a single action, with an explicit confirmation step before deletion occurs.
- **FR-024**: Users MUST be able to export a complete, human-readable copy of everything currently remembered about them.
- **FR-025**: Users MUST be able to enable or disable memory on a per-category basis.
- **FR-026**: System MUST permanently delete all memories belonging to a user when that user's account is deleted.

**Security & access control**

- **FR-027**: System MUST ensure a user can only ever view, use, or modify their own memories — memory content MUST NOT be exposed to or usable by any other user.
- **FR-028**: System MUST record an audit trail of memory creation, updates, approvals, rejections, and deletions sufficient to answer "what changed, when, and how" for support and compliance purposes, without exposing that audit trail's contents to other users.
- **FR-029**: System MUST treat all memory content — including content originally derived from conversation analysis — as data, never as instructions, when it is later reused; a memory MUST NOT be able to alter the AI's operating instructions or bypass safety/content rules.

**Performance & scale**

- **FR-030**: System MUST continue to perform relevant-memory selection for a conversation turn without noticeably delaying the start of the AI's response, even as the number of stored memories for a user grows into the thousands.
- **FR-031**: System MUST support background cleanup of memories that have expired or become stale (e.g., no longer reinforced, superseded, or past a defined retention point) without requiring the user to manually prune them.

### Key Entities

- **Memory**: A single remembered fact or preference — its content, category, lifecycle state, and ranking attributes (importance, confidence, recency, frequency, source, expiration).
- **Memory Category**: The classification of a memory (user preference, personal fact, project context, conversation-derived context; knowledge-derived facts reserved for future support).
- **Memory Source**: The origin of a memory — which conversation, explicit user action, or (in the future) integration/agent produced it.
- **Memory Version / History Entry**: A record of a prior state of a memory, captured whenever the memory's content changes, enabling "what did this used to say and when did it change."
- **Memory Approval**: The pending/approved/rejected decision associated with a candidate memory, including who/what made the decision and when.
- **Memory Access/Audit Record**: A log entry capturing who accessed or changed a memory and when, for security and compliance review.
- **Memory Preference**: A user's account-level and per-category settings for the memory system (enabled/disabled, approval mode per category).
- **Project**: A user-created, named workspace that groups a set of related conversations, used to scope Project Memory so it applies only within that workspace; a minimal grouping construct introduced by this feature, not a full project-management entity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a follow-up conversation started at least one day after a fact was stated, Lucy's response correctly reflects that fact without the user needing to restate it, for at least 90% of previously stated stable preferences/facts.
- **SC-002**: Users can find, review, and act on (edit, delete, approve, or reject) any given memory in the Memory Center in under 30 seconds from opening it.
- **SC-003**: A user can fully disable memory, or clear all of their stored memories, in three or fewer actions, with the effect visible immediately.
- **SC-004**: At least 95% of memory candidates flagged as containing sensitive personal information are correctly held for manual review rather than auto-approved.
- **SC-005**: Zero instances, across security testing, of one user's memory content being visible to or usable by another user.
- **SC-006**: Selecting relevant memories for a conversation turn adds no perceptible delay to the start of Lucy's response, as judged by users in usability testing, even for accounts with thousands of stored memories.
- **SC-007**: Users report increased satisfaction with response relevance/personalization in follow-up surveys after the feature ships, compared to before.
- **SC-008**: Support/compliance requests to reconstruct "what did Lucy know about me and when" can be answered completely from the audit trail without engineering involvement.

## Assumptions

- Memory is a per-individual-user capability in this release; team/organization-shared memory is explicitly out of scope and reserved for a future iteration, consistent with the source request's "Organizational memory (future)" framing.
- Knowledge Memory (entities/relationships/facts/events) is explicitly deferred to a future iteration per the source request; this specification covers User, Personal, Project, and Conversation memory only.
- Import of previously exported memories is explicitly deferred to a future iteration per the source request ("import memories in the future"); this release covers export only.
- "Short-term memory" (current conversation context) is already served by the existing Chat Engine's conversation history and is not re-implemented by this feature; this specification's "memory" refers to medium- and long-term memory that persists across conversations.
- Default retention has no fixed expiration unless a memory is explicitly time-bound (e.g., tied to a project that has ended); memories otherwise persist until the user deletes them, an account is deleted, or a background cleanup process retires a stale/superseded memory per FR-031.
- Export format is a complete, structured, human-readable file (exact format is an implementation decision for the planning phase, not a product-level constraint).
- "Millions of memories" and "thousands of stored memories per user" in this specification describe the scale the system must remain responsive at, not a hard ceiling on how many memories a single user may accumulate.
- "Project" is a new concept introduced by this feature (there is no existing Project entity in Ask Lucy today), scoped deliberately to the minimum needed for Project Memory: a name and its member conversations. A richer Projects workspace (files, custom instructions, sharing) is a plausible future feature but is not implied or required by this specification.
- Because memory candidates are created via passive, automatic analysis of conversation content and, by default, activated automatically, the exact analysis timing/frequency (e.g., near-real-time vs. periodic batch) is an implementation decision for the planning phase; this specification requires only that the user-visible signal (FR-006a) and full editability (User Story 2) are never skipped regardless of that timing.
- Memory is unmetered in this release: it is available uniformly across all subscription tiers (Free/Professional/Enterprise), and storage/usage tracking for billing purposes is out of scope. Billing Engine integration, if ever needed, belongs to a future iteration.
