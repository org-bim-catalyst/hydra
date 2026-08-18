# Feature Specification: Prompt Library & Prompt Engineering Workspace

**Feature Branch**: `019-prompt-library-workspace`

**Created**: 2026-08-10

**Status**: Draft

**Input**: User description: "Build a reusable Prompt Library and Prompt Engineering Workspace for Ask Lucy. The system must allow users to create, organize, version, test, reuse, share, and manage prompts independently from individual conversations. Prompts must be reusable across chat conversations, knowledge bases, RAG workflows, AI models, and (in future specifications) AI agents and automation workflows. The implementation must integrate with existing Conversation Management, the Multi-Provider AI Engine, Knowledge Base Management, the Document Intelligence Pipeline, the RAG Engine, and the AI Memory System, without duplicating any of them. AI Agents and Workflow Automation are explicitly out of scope for this specification."

## Clarifications

### Session 2026-08-10

- Q: Should Prompt Folders support nested sub-folders (arbitrary depth), or be a single flat level per user? → A: Nested folders (arbitrary-depth parent/child hierarchy).
- Q: Must a prompt's name be unique within its owner's library, or can a user have multiple prompts with the same name? → A: Unique per user — system blocks or auto-suggests a rename on a duplicate.
- Q: When two edits to the same prompt are saved concurrently (e.g. two browser tabs), what should happen to the second save? → A: Reject with a conflict error (optimistic concurrency); no auto-merge, no silent overwrite.
- Q: What counts as "usage" for a prompt's usage count and "recently used" ordering? → A: Successful executions only (testing workspace or conversation insertion that completes successfully).
- Q: Can a user export multiple prompts in a single operation/file, or is export limited to one prompt at a time? → A: Multi-select bulk export — several prompts can be bundled into one export file.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create and Reuse a Structured Prompt (Priority: P1)

A user who repeatedly asks the AI to perform the same kind of task (e.g., "summarize a technical document into a target language at a target length") wants to save that instruction once, as a named, reusable prompt with placeholders for the parts that change each time, instead of retyping or copy-pasting a slightly different version of the same instruction into every new conversation.

**Why this priority**: This is the core value proposition of the feature — turning ad hoc, disposable chat text into a durable, reusable asset. Without this, nothing else in the feature (versioning, testing, sharing readiness) has a reason to exist. It is the minimum slice that is independently valuable.

**Independent Test**: Can be fully tested by creating a prompt with a system instruction, a user instruction containing `{{document}}`, `{{target_language}}`, and `{{summary_length}}` placeholders, saving it, then reopening it later and confirming the content, variable definitions, and metadata persisted exactly as entered — delivers value even if no other capability in this spec exists yet.

**Acceptance Scenarios**:

1. **Given** a user on the Prompt Library with no existing prompts, **When** they create a new prompt with a name, a system instruction, a user instruction containing variable placeholders, and save it, **Then** the prompt appears in their library with the entered content and an automatically-detected list of variables.
2. **Given** a saved prompt, **When** the user reopens it, **Then** every field (system instructions, user instructions, variables and their metadata, category, tags) matches what was last saved.
3. **Given** a user editing a prompt, **When** they define a variable named `document` and mark it required with no default value, **Then** the system records that variable's name, type, required flag, and description as entered.
4. **Given** two users each with their own prompt library, **When** either user lists or searches prompts, **Then** each user sees only prompts they own — no cross-user visibility.

---

### User Story 2 - Test a Prompt Before Relying on It (Priority: P1)

A user drafting or refining a prompt wants to try it out immediately — supplying sample variable values, picking a provider and model, running it, and seeing the actual AI output, token usage, and cost — without leaving the prompt they're editing or polluting a real conversation with test traffic.

**Why this priority**: A prompt a user cannot verify before use is not trustworthy as a "reusable product asset." Testing is what separates this feature from a plain text-snippet library and is necessary to validate that variables and instructions actually work together. This must ship alongside creation (P1) for the feature to be usable, not merely storable.

**Independent Test**: Can be fully tested by opening a saved prompt in the testing workspace, filling in variable values, selecting a provider/model, executing, and confirming a streamed response appears along with token usage and estimated cost — independent of versioning or sharing capabilities.

**Acceptance Scenarios**:

1. **Given** a saved prompt with required variables, **When** the user opens the testing workspace and leaves a required variable blank, **Then** execution is blocked with a clear message identifying which variable is missing, and no AI call is made.
2. **Given** a saved prompt with all required variables filled in, **When** the user selects a provider and model and executes, **Then** the response streams into the output panel and, on completion, token usage, estimated cost, latency, provider, and model are displayed alongside it.
3. **Given** a completed test execution, **When** the user chooses to save it as a test case, **Then** the input variable values, model/provider selection, and resulting output are stored for future reuse and comparison.
4. **Given** a prompt that declares a required capability (e.g., structured JSON output), **When** the user attempts to select a model that does not support it, **Then** that model is excluded or flagged as incompatible before execution.

---

### User Story 3 - Version, Compare, and Restore Prompt Changes (Priority: P2)

A user iterating on a prompt over time wants every meaningful edit preserved as a distinct version, wants to see what changed between two versions, and wants the ability to roll back to an earlier version if a later change made outputs worse — without ever losing the history.

**Why this priority**: Prompts are iterated on heavily in practice ("prompt engineering" implies experimentation). Losing the ability to recover a prior working version, or to understand what changed and why quality shifted, undermines trust in the whole library. This depends on P1 (a prompt must exist to be versioned) but is not required for the very first usable release.

**Independent Test**: Can be fully tested by editing a saved prompt's content twice, confirming two distinct versions exist with the original content intact, requesting a comparison between them, and restoring the first version to confirm the prompt's active content reverts while both versions remain in history.

**Acceptance Scenarios**:

1. **Given** a saved prompt, **When** the user modifies its content and saves, **Then** a new version is created that records the content, variables, model settings, author, timestamp, and an optional change description, while the prior version remains retrievable unchanged.
2. **Given** a prompt with three versions, **When** the user selects any two versions to compare, **Then** the differences between their content, variables, and model settings are clearly presented.
3. **Given** a prompt with an older version selected, **When** the user restores that version, **Then** the prompt's active/current state becomes identical to the restored version's content, and this restoration itself is recorded as a new version (history is never deleted or overwritten).
4. **Given** any prompt version in history, **When** the user requests it, **Then** it can be viewed in full or duplicated into a brand-new, independent prompt without altering the original.

---

### User Story 4 - Organize and Find Prompts at Scale (Priority: P2)

A user who has accumulated many prompts wants to organize them into folders and categories, tag them, mark favorites and pins, and quickly search or filter to find the right one — so the library stays usable as it grows into the hundreds or thousands of entries.

**Why this priority**: Organization and search are what keep the library usable past the first few prompts; without it, the feature degrades into an unsearchable pile once adoption grows. It builds on P1 (prompts must exist) and is needed before the library scales, but a single user validating the concept can work without it briefly.

**Independent Test**: Can be fully tested by creating a set of prompts across different categories and tags, placing some into folders, favoriting and pinning a subset, then verifying that search-by-keyword, filter-by-tag/category, and the favorites/pinned/recently-used views each return the expected, correctly-scoped results.

**Acceptance Scenarios**:

1. **Given** a library with prompts spread across multiple categories and tags, **When** the user searches by a keyword that appears in a prompt's name, description, content, or a variable name, **Then** matching prompts are returned, ranked with the most relevant matches first.
2. **Given** a set of prompts, **When** the user filters by category, tag, or folder (individually or combined), **Then** only prompts matching all applied filters are shown.
3. **Given** a prompt, **When** the user marks it as a favorite or pins it, **Then** it subsequently appears in the corresponding favorites/pinned view, and unmarking removes it from that view without affecting the prompt itself.
4. **Given** prompt usage over time, **When** the user opens the "recently used" or "recently modified" view, **Then** prompts are listed in the correct recency order based on timestamps of successful executions or edits respectively — failed or blocked executions do not affect "recently used" ordering.
5. **Given** a library of at least one thousand prompts, **When** the user performs a search or applies filters, **Then** results are returned and displayed without the user perceiving the library as slow, using pagination or lazy loading rather than loading the entire library at once.

---

### User Story 5 - Use a Saved Prompt Inside a Live Conversation (Priority: P2)

A user in the middle of a chat conversation wants to insert one of their saved prompts, have its variables resolved (prompting them for values where needed), and have it drive the next message — without breaking the flow of the conversation or losing prior context.

**Why this priority**: Reuse "inside conversations" is one of the two headline reuse targets called out for this feature (the other being RAG/testing) and is what turns a standalone library into something that changes daily chat behavior. It depends on P1–P2 (a working, resolvable prompt) but is a distinct integration surface that can ship after the standalone workspace is solid.

**Independent Test**: Can be fully tested by opening an existing conversation, inserting a saved prompt that has variables, supplying values when prompted, sending it, and confirming the conversation continues using the resolved prompt content, the conversation's already-selected provider/model, and existing conversation context — independent of the versioning or organization features.

**Acceptance Scenarios**:

1. **Given** an active conversation and a saved prompt with variables, **When** the user inserts the prompt, **Then** they are prompted to supply values for any variables not already resolvable from context, before the message is sent.
2. **Given** a prompt inserted into a conversation, **When** it is sent, **Then** the conversation's existing provider/model selection and prior message context are preserved, and the resolved prompt text becomes the new user message.
3. **Given** a prompt that specifies model capability requirements incompatible with the conversation's currently selected model, **When** the user attempts to insert it, **Then** they are warned before the message is sent.

---

### User Story 6 - Request RAG or Memory Context From a Prompt (Priority: P3)

A user building a prompt whose instructions depend on retrieved documentation (e.g., "answer using only the provided technical documentation") or on the user's own stored preferences/facts wants the prompt to be able to declare that it needs that context, and have it assembled automatically at execution time from the existing Knowledge Base/RAG and Memory systems — without the prompt author having to manually copy content in each time.

**Why this priority**: This is high-value but narrower — it only benefits prompts that specifically need retrieval or memory augmentation, and it is explicitly built on top of (not a replacement for) the existing RAG and Memory engines. It is reasonable to ship after the core authoring/testing/versioning/organization loop is solid.

**Independent Test**: Can be fully tested by creating a prompt flagged to use a specific knowledge base and executing it, confirming retrieved context is assembled into the request sent to the AI provider and is visibly distinguishable from the prompt's own instructions and the user's variable values; and separately, by flagging a prompt to use memory context and confirming relevant stored user preferences are included the same way.

**Acceptance Scenarios**:

1. **Given** a prompt configured to reference a specific knowledge base, **When** it is executed, **Then** relevant retrieved passages are fetched from the existing RAG engine and included as clearly-labeled context distinct from the prompt's system/user instructions.
2. **Given** a prompt configured to use memory context, **When** it is executed, **Then** relevant stored user memory is retrieved via the existing Memory system and included as clearly-labeled context, without the prompt author having re-implemented memory retrieval.
3. **Given** a prompt using both RAG and memory context, **When** the assembled request is constructed, **Then** system instructions, developer instructions, the prompt template, user-supplied variable values, retrieved RAG context, and retrieved memory context remain distinguishable from one another, and none of the retrieved or user-supplied content can override or replace the system-level instructions.

---

### User Story 7 - Export and Import Prompts (Priority: P3)

A user wants to export one or more selected prompts — a single prompt, or several selected at once — to a portable file (e.g., to back them up, move them between environments, or share a prompt file with a colleague outside the product), and wants to import a previously-exported prompt file back in, with the system validating it before creating anything.

**Why this priority**: Valuable for portability and backup, and it directly prepares the metadata shape needed for a future marketplace, but it is not required for a user to get value from creating, testing, versioning, or organizing prompts, so it is reasonable to ship last within this specification.

**Independent Test**: Can be fully tested by exporting a prompt (with its variables, current version, and model settings) to a file, deleting or renaming the original, importing the exported file back in, and confirming the recreated prompt matches the original in content, variables, and settings; separately, by selecting several prompts and exporting them into a single bundled file, then importing it and confirming every prompt in the bundle is recreated independently; and separately, by attempting to import a malformed or invalid file and confirming it is rejected with a clear error before any prompt is created.

**Acceptance Scenarios**:

1. **Given** a saved prompt, **When** the user exports it, **Then** a file is produced containing the prompt's metadata, content, variables, current version, model settings, and tags.
2. **Given** multiple selected prompts, **When** the user exports them together, **Then** a single file is produced bundling each selected prompt's full export data as an independent entry within it.
3. **Given** a previously-exported prompt file (single or bundled), **When** the user imports it, **Then** the system validates its structure and the content of every entry before creating any prompt, and rejects the entire import with a specific, actionable error — creating nothing — if any entry fails validation.
4. **Given** an imported prompt, **When** it is created, **Then** it is owned by the importing user and starts its own independent version history — it does not silently merge with or overwrite any existing prompt.

---

### Edge Cases

- What happens when a user executes a prompt and the AI provider call fails or times out mid-stream? The failure MUST be surfaced to the user with a clear, actionable message (per platform-wide no-silent-failure requirements) — never a silently empty or partial output presented as complete.
- What happens when a prompt references a variable in its content (e.g., `{{project_name}}`) that has no corresponding variable definition? The system must flag this as a validation issue before the prompt can be executed or saved as ready-to-use.
- What happens when a required variable's supplied value fails its type/format/length/allowed-value validation at execution time? Execution must fail gracefully with a specific message identifying the offending variable and rule, and no AI call is made.
- What happens when a user attempts to restore a prompt version whose declared model/provider is no longer available or enabled? The restore must succeed for content/variables, and the system must surface that the referenced model/provider is currently unavailable, prompting the user to pick a current one before execution.
- What happens when a user duplicates a prompt or a specific version? A fully independent copy is created, owned by the requesting user, with its own version history starting fresh — it is never linked back to the source such that edits to one affect the other.
- What happens when an imported prompt file's schema is unrecognized, corrupted, or contains a version of the export format the system doesn't understand? Import is rejected outright with a clear validation error; nothing partial is created.
- What happens when a prompt is archived while it is actively referenced/pinned in a conversation draft? The archived prompt remains usable for that in-flight action but no longer appears in default library listings, and can be restored later.
- What happens when a user deletes a prompt that has execution history, saved test cases, or ratings attached? Deletion must not silently orphan or expose that history to other users; the platform's standard retention/soft-delete approach applies so the audit trail is preserved.
- What happens when two edits to the same prompt are saved concurrently (e.g., two browser tabs)? The second save MUST be rejected outright with a clear conflict error; the user must reload the current state and reapply their change — no silent overwrite and no automatic merge.
- What happens when a user tries to create, duplicate, or import a prompt whose name already exists in their own library? The system MUST block the save (or the specific import entry) and either require a different name or auto-suggest a non-conflicting name — it MUST NOT silently create a second prompt with an identical name for the same owner.
- What happens when a user tries to move a folder into one of its own sub-folders? The system MUST reject the move as it would create a cycle in the folder hierarchy.
- What happens when a variable is typed as `Knowledge Base` or `Conversation` but the referenced knowledge base/conversation has since been deleted or is no longer accessible to the user? Execution must fail gracefully with a message identifying the missing reference, not silently proceed without that context.
- What happens when a prompt's content is extremely large (well beyond typical usage)? The editor must remain usable (no freezing/data loss) and the user must see a clear estimate of token/character size relative to the limits of the models they might select.

## Requirements *(mandatory)*

### Functional Requirements

**Prompt authoring & structure**

- **FR-001**: System MUST allow users to create, edit, duplicate, delete, archive, and restore prompts.
- **FR-002**: A prompt MUST preserve the distinction between its structural components: system instructions, developer instructions, user instructions, context, examples, variables, output instructions, constraints, and metadata — these are stored and displayed as separate, identifiable parts, never collapsed into a single opaque text blob.
- **FR-003**: System MUST support the following prompt types: Chat Prompt, System Prompt, Instruction Prompt, Summarization Prompt, Translation Prompt, Extraction Prompt, Classification Prompt, RAG Prompt, and Structured Output Prompt.
- **FR-004**: System MUST allow a prompt to declare required or preferred AI model capabilities (e.g., vision input, structured/JSON output) and MUST prevent execution against a model that does not meet a declared *required* capability.
- **FR-005**: Users MUST be able to preview a prompt (its resolved appearance with example or default variable values) without executing it against an AI provider.
- **FR-006**: A prompt's name MUST be unique within its owning user's library (case-insensitive); the system MUST block a create, duplicate, or import operation that would produce a duplicate name for the same owner and MUST offer the user a way to rename or auto-generate a non-conflicting name instead.
- **FR-007**: When two update operations on the same prompt are submitted concurrently, the system MUST reject the second save with an explicit conflict error rather than silently overwriting the first — no automatic merge is performed; the user must reload the current state before reapplying their change.

**Variables**

- **FR-010**: System MUST support named variables referenced in prompt content using a distinguishable placeholder syntax (e.g., `{{variable_name}}`), automatically detected from the prompt's text.
- **FR-011**: Each variable MUST support the following metadata: name, description, type, required flag, default value, example value, and validation rules.
- **FR-012**: System MUST support the following variable types: String, Number, Boolean, Date, JSON, Text (long-form), File, Conversation, and Knowledge Base.
- **FR-013**: System MUST validate variable values against required-ness, type, length, format, and allowed-value rules before a prompt is executed, and MUST reject execution with a specific, per-variable error when validation fails — no partial or best-effort execution with invalid input.
- **FR-014**: System MUST detect and flag, before execution, any variable placeholder present in prompt content with no corresponding variable definition, and any defined variable that is never referenced in the content.

**Templates**

- **FR-020**: Users MUST be able to save a prompt as a reusable template and execute it repeatedly with different variable values without modifying the saved template itself.

**Versioning**

- **FR-030**: System MUST create a new, immutable version record every time a prompt's content, variables, or model settings are saved as a change (not on every keystroke of an unsaved draft).
- **FR-031**: Each version MUST record: version number, content, variables, model settings, author, created date, and an optional change description.
- **FR-032**: System MUST allow users to view any historical version, compare any two versions and see their differences, restore a prior version as the new active version, and duplicate any version into a new, independent prompt.
- **FR-033**: System MUST NOT ever permanently overwrite or delete a historical version through normal user operations; restoring a prior version creates a new version rather than erasing intervening history.

**Execution & testing**

- **FR-040**: System MUST allow users to execute a prompt directly from the prompt workspace, supplying provider, model, temperature, maximum output tokens, and (where applicable) structured output mode.
- **FR-041**: Prompt execution MUST stream the AI response to the user as it is generated, where the selected provider/model supports streaming.
- **FR-042**: System MUST display, for each execution, the AI provider used, the model used, token usage, estimated cost, and latency.
- **FR-043**: Users MUST be able to save an executed test (its input variable values, provider/model, and resulting output) as a reusable test case, optionally with expected output and evaluation criteria.
- **FR-044**: Users MUST be able to mark a test execution's output as Good, Needs Improvement, or Failed as a manual evaluation.
- **FR-045**: System MUST allow users to run and visually compare multiple executions of the same or different prompt versions side by side (e.g., same prompt against two different models, or two different prompt versions against the same model), with each execution's provider, model, and settings clearly distinguished in the comparison.
- **FR-046**: Prompt execution MUST use the platform's existing multi-provider AI abstraction exclusively — the prompt system MUST NOT call any AI provider directly or embed provider-specific logic.

**Organization & discovery**

- **FR-050**: System MUST allow users to organize prompts into folders, assign categories and tags, mark prompts as favorites, and pin prompts.
- **FR-051**: System MUST provide a "recently used" view ordered by the timestamp of each prompt's most recent *successful* execution (failed or blocked executions do not count), and a "recently modified" view ordered by edit timestamp.
- **FR-052**: System MUST support searching prompts by name, description, content, tags, category, author, and variable names, and support filtering and sorting results by these same attributes plus favorite/pinned/archived status.
- **FR-053**: Search and list operations MUST remain responsive for a library containing at least several thousand prompts per user, using pagination or lazy loading rather than retrieving an entire library at once.
- **FR-054**: Folders MUST support nested sub-folders to an arbitrary depth within a single user's library; the system MUST prevent a folder from being moved into its own descendant (no cycles).

**Ownership & sharing readiness**

- **FR-060**: In this specification, every prompt MUST be private to its owning user — no other user can view, list, search, execute, or modify a prompt they do not own.
- **FR-061**: Prompt data MUST be modeled so that a future permission model (owner, viewer, editor, publisher, administrator) and future sharing scopes (team, organization, public, marketplace) can be added without restructuring existing prompt or version data — this specification does not implement any sharing or multi-user access, only the ownership foundation it will build on.
- **FR-062**: Prompt metadata MUST capture, from initial creation, the fields a future marketplace listing would need: name, description, author, category, tags, version, usage count, compatibility (required/preferred capabilities), created date, and updated date. Rating is recorded as a data point per FR-044/FR-062 metadata shape but marketplace display/aggregation of ratings is out of scope for this specification.

**Import & export**

- **FR-070**: Users MUST be able to export a single prompt, or multiple selected prompts bundled into one file, containing each prompt's metadata, content, variables, current version, model settings, and tags.
- **FR-071**: System MUST validate an imported file's structure and the content of every prompt entry it contains (whether the file holds one prompt or a bundle) before creating any prompt record, and MUST reject the entire import — creating nothing — with a specific, actionable error if the file or any entry within it is invalid or unrecognized.
- **FR-072**: Each imported prompt MUST be created as a new, independent prompt owned by the importing user, with its own version history — it MUST NOT be linked to or silently merged with any existing prompt. If an imported prompt's name conflicts with an existing prompt in the importing user's library, the same uniqueness handling as FR-006 applies.

**Conversation, RAG, and Memory integration**

- **FR-080**: Users MUST be able to insert a saved prompt into an active chat conversation; the system MUST resolve the prompt's variables (prompting the user for any values not already available), apply the prompt's own configuration, and use the conversation's existing provider/model selection and prior context.
- **FR-081**: A prompt MUST be able to optionally request context from the existing Knowledge Base/RAG engine at execution time; this specification MUST reuse the existing RAG retrieval capability and MUST NOT implement a separate or duplicate retrieval mechanism.
- **FR-082**: A prompt MUST be able to optionally request relevant context from the existing AI Memory system at execution time; this specification MUST reuse the existing memory retrieval capability and MUST NOT implement a separate or duplicate memory mechanism.
- **FR-083**: When a prompt's execution assembles system instructions, developer instructions, the prompt template, user-supplied variable values, retrieved RAG context, and retrieved memory context into a single request, these components MUST remain structurally distinguishable from one another in how the request is assembled, and none of the variable, RAG, or memory content may be capable of overriding or replacing the system-level instructions.

**Security & authorization**

- **FR-090**: Every prompt operation (create, read, update, delete, archive, restore, duplicate, version, execute, test, export, import) MUST require an authenticated user and MUST be authorized against that user's ownership of the prompt.
- **FR-091**: Prompt content MUST NOT appear in logs accessible to anyone other than the owning user and authorized platform operators, and MUST NOT be logged at all in production observability sinks by default.
- **FR-092**: Variable values supplied at execution time, and any content retrieved via RAG or Memory, MUST be treated as untrusted input — never capable of being interpreted as system-level or developer-level instructions that change the AI's behavior beyond what the prompt author authorized.
- **FR-093**: AI provider credentials MUST remain server-side and MUST NOT be exposed to the client at any point in prompt creation, testing, or execution.

**Observability**

- **FR-100**: System MUST record, per prompt execution: prompt and version identifiers, provider, model, latency, token usage, estimated cost, and outcome (success/error), without logging sensitive prompt content by default.
- **FR-101**: Every execution failure (provider error, timeout, validation failure) MUST be surfaced to the requesting user with a clear, actionable message — no silent failures, partial results presented as complete, or unhandled errors.

### Key Entities

- **Prompt**: The reusable asset itself — owner, name (unique per owner, case-insensitive), description, prompt type, current content (system/developer/user instructions, context, examples, output instructions, constraints), status (draft/active/archived), category, tags, folder membership, favorite/pinned flags, model compatibility requirements, and marketplace-ready metadata (usage count, rating placeholder, compatibility). Has exactly one owner in this specification.
- **PromptVersion**: An immutable snapshot of a Prompt's content, variables, and model settings at a point in time — version number, content, variables snapshot, model settings snapshot, author, created date, and optional change description. A Prompt has many PromptVersions; one is always the current/active version.
- **PromptVariable**: A named placeholder within a Prompt's content — name, description, type, required flag, default value, example value, and validation rules. Belongs to a specific PromptVersion (or the Prompt's current definition).
- **PromptCategory**: A single classification a Prompt belongs to (e.g., "Summarization", "BIM Documentation") — supports organization and filtering.
- **PromptTag**: A free-form label a Prompt can carry, many-to-many with Prompt, used for search and filtering.
- **PromptFolder**: A user-defined grouping a Prompt can be placed into, owned by the same user as the prompts it contains. Folders support nested sub-folders to an arbitrary depth (a folder may reference a parent folder); the system prevents cycles (a folder cannot become its own ancestor).
- **PromptTestCase**: A saved, reusable test scenario for a Prompt — input variable values, optional expected output, evaluation criteria, and the model/provider it was defined against.
- **PromptExecution**: A record of one run of a Prompt (or PromptVersion) against a provider/model — resolved variable values used, provider, model, generation parameters (temperature, max tokens), whether RAG/Memory context was requested, and timing.
- **PromptExecutionResult**: The outcome of a PromptExecution — the AI output, token usage (input/output), estimated cost, latency, success/error status, and any error detail surfaced to the user.
- **PromptRating**: A manual evaluation of a PromptExecutionResult or PromptVersion — Good, Needs Improvement, or Failed, recorded by the user who performed the evaluation.
- **PromptUsageStatistics**: Aggregated usage data for a Prompt, counting *successful* executions only — successful-execution count, last-successful-use timestamp, and other metrics needed to power "recently used" views and future marketplace signals.
- **PromptAuditLog**: An immutable record of security- and lifecycle-relevant actions taken on a Prompt (create, update, delete, archive, restore, export, import, permission change) — who did what, when.
- **PromptPermission** *(data model only, not enforced beyond owner in this spec)*: The future association between a Prompt, a grantee, and a role (Owner, Viewer, Editor, Publisher, Administrator).
- **PromptShare** *(data model only, not enforced in this spec)*: The future association representing a Prompt shared into a scope beyond its owner (team, organization, public, marketplace).
- **PromptEvaluation** *(data model only, not implemented in this spec)*: The future record of an automated/LLM-as-judge evaluation of a Prompt's output quality, distinct from the manual PromptRating implemented here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can create a new, reusable, variable-driven prompt from scratch in under 3 minutes.
- **SC-002**: A user can go from opening a saved prompt to seeing a streamed test result (with token usage and cost) in under 60 seconds, assuming variable values are already known.
- **SC-003**: A user can locate a specific prompt from a library of 1,000+ prompts, via search or filters, in under 10 seconds.
- **SC-004**: 100% of prompt executions with a missing required variable or a variable failing validation are blocked before any AI provider call is made, with a clear, specific error identifying the problem.
- **SC-005**: 100% of historical prompt versions remain fully viewable and restorable indefinitely — no user-initiated edit ever destroys prior version history.
- **SC-006**: A user can insert a saved prompt into an active conversation and have it produce a response without losing or resetting the conversation's prior context, 100% of the time.
- **SC-007**: A user can export a prompt and successfully re-import it (as a new, independent, functionally identical prompt) with zero manual correction needed, in the common case of a well-formed export.
- **SC-008**: 100% of prompts, versions, and executions are visible only to their owning user — zero instances of one user accessing another user's prompt content, variables, or execution history.
- **SC-009**: Users report that comparing two executions of the same prompt (different models, providers, or versions) makes the difference between them clear without needing to cross-reference separate screens.
- **SC-010**: 100% of prompt execution failures (provider error, timeout, validation failure) are surfaced to the user with an actionable message — zero silent failures or unexplained blank results.

## Assumptions

- "Ask Lucy" already has, or will have prior to or alongside this feature, working implementations of: Conversation Management, the Multi-Provider AI Engine (with a provider/model abstraction), Knowledge Base Management, the Document Intelligence Pipeline, the RAG Engine, and the AI Memory System — this feature integrates with and does not rebuild any of these.
- "Users" in this specification are individual, authenticated platform users; no organization/team/tenant concept beyond the existing per-user account model is assumed or introduced.
- The private-ownership model (FR-060) is the correct default for an initial release aimed at individual power users doing prompt engineering; broader sharing is explicitly deferred per the request's own scope boundary, not omitted by oversight.
- "Thousands of prompts" (Performance) is interpreted as thousands *per user*, not a single global ceiling across all users — this is the reasonable reading of a personal prompt library at enterprise scale.
- Manual evaluation (Good / Needs Improvement / Failed) is sufficient for this specification's testing workspace; automated/LLM-as-judge evaluation is explicitly future work per the request.
- Estimated cost display (FR-042) relies on provider-published or platform-configured pricing data already available to the Multi-Provider AI Engine; this feature consumes that data rather than sourcing pricing independently.
- AI Agents, MCP tool orchestration, and Workflow Automation are out of scope, as explicitly stated in the request; any prompt "usable by a future agent" requirement is satisfied by this feature's data model being agent-agnostic and reusable, not by building agent integration now.
- Prompt Marketplace, team/organization collaboration, automated LLM evaluation, billing, and subscription management are out of scope, as explicitly stated in the request; this feature only prepares the data shape (metadata, permission/share entities as data-model-only) for those to be added later without a rebuild.
- Voice output, if a prompt's result is read aloud, follows the platform-wide consistent voice persona requirement already established elsewhere in the product and is not redefined by this feature.
- "Thousands of prompts" combined with "fast search" implies server-side search/filtering with pagination is the expected experience; a client-side-only search over an unbounded prompt set would not meet SC-003 at scale and is therefore not assumed as the approach.
