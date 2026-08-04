# Feature Specification: Knowledge Base Management

**Feature Branch**: `014-knowledge-base-management`

**Created**: 2026-08-04

**Status**: Draft

**Input**: User description: "Introduce a comprehensive Knowledge Base Management system that allows users to organize, manage, and prepare documents for future Retrieval-Augmented Generation (RAG). This specification focuses exclusively on knowledge organization and lifecycle management. It does NOT include embedding generation, vector search, semantic retrieval, or prompt augmentation. A Knowledge Base is a logical container that groups related documents for a specific purpose (project, department, customer, topic). Users create unlimited knowledge bases, organize documents into folders, categorize and tag them, and manage their lifecycle (draft, active, archived, soft-deleted) through a dedicated dashboard with search, filtering, sorting, favorites, and pinning."

## Clarifications

### Session 2026-08-04

- Q: After a knowledge base is soft-deleted, how should permanent deletion happen? → A: Auto-purge after a fixed retention period (30 days from the soft-delete date), similar to a recycle bin. The owner may still request immediate permanent deletion with explicit confirmation before the retention window elapses.
- Q: When a user duplicates a knowledge base, what should be copied? → A: Deep copy — the new knowledge base gets its own independent copy of the folder tree and all document associations/files, fully usable immediately without the user re-adding documents.
- Q: When a user creates a custom category, who can see and use it? → A: Private to the creating user — custom categories only appear in that user's own category list and filters; they do not become visible to other users. The predefined default categories (Engineering, Architecture, etc.) remain shared platform-wide.

### Session 2026-08-04 (follow-up)

- Q: When a knowledge base is permanently purged (30-day retention elapses, or the owner purges it immediately), what happens to the underlying document files it references? → A: Cascade delete — permanently purging the knowledge base also permanently deletes its associated document files from storage via the File Management Engine, since each document belongs to exactly one knowledge base and has no other owner once its knowledge base is gone.
- Q: When a knowledge base is duplicated, does each duplicated document get its own independent physical file copy, or reference the original's file content? → A: Independent physical copies — duplication writes a fully independent physical file for every document, so a later purge of either the original or the copy can never affect the other and no cross-knowledge-base reference bookkeeping is needed.
- Q: The original request specified WCAG 2.2 AA, keyboard navigation, screen reader support, high contrast, and responsive layouts for the workspace UI, but the initial spec draft omitted these — should they be added as formal requirements of this spec? → A: Yes — added as formal, testable functional requirements and a success criterion (see FR-039–FR-042, SC-010) rather than deferred to a separate design-system spec.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create and manage a knowledge base's core lifecycle (Priority: P1)

A user creates a new knowledge base to hold documents for a specific purpose (e.g., "BIM Standards"), gives it a name and description, and can later rename it, edit its details, or delete it when it's no longer needed.

**Why this priority**: This is the atomic unit of the entire feature. Without the ability to create, edit, and delete a knowledge base, no other capability (organization, discovery, statistics) has anything to operate on. It is independently valuable: a user can create a container and start referencing it even before any document-organization or dashboard polish exists.

**Independent Test**: Can be fully tested by creating a knowledge base with a name and description, confirming it appears in the user's list, editing its name/description/color/icon, and deleting it — with each step's result immediately visible without needing any other feature in this spec.

**Acceptance Scenarios**:

1. **Given** a user is on the Knowledge Base workspace, **When** they create a new knowledge base with a name, **Then** the knowledge base is saved with a "Draft" status, owned by that user, and appears in their list immediately.
2. **Given** a user owns a knowledge base, **When** they edit its name, description, color, or icon, **Then** the changes are saved and reflected everywhere the knowledge base is displayed, and its "Last Updated" timestamp changes.
3. **Given** a user owns a knowledge base, **When** they delete it, **Then** the knowledge base is soft-deleted (marked deleted, hidden from normal views) rather than immediately and permanently removed.
4. **Given** a user attempts to create a knowledge base without a name, **When** they submit the form, **Then** the system rejects the submission with a clear, actionable validation message.
5. **Given** a user does not own a knowledge base, **When** they attempt to edit or delete it directly (e.g., via API), **Then** the system denies the action.

---

### User Story 2 - Organize documents into folders within a knowledge base (Priority: P1)

A user opens a knowledge base and organizes its documents into folders and subfolders (e.g., "2026 Contracts" > "Client A"), so that related material stays grouped and easy to browse as the collection grows.

**Why this priority**: Organization is the feature's core value proposition — a knowledge base that cannot be internally structured is just a flat, unmanageable bucket. This is tied with Story 1 as foundational because the "Vision" and "Organization" requirements treat folder structure as intrinsic to what a knowledge base is, not an enhancement.

**Independent Test**: Can be fully tested by creating a folder inside a knowledge base, creating a subfolder inside it, assigning documents to a folder, and moving a document between folders — each producing an immediately visible change in the folder tree, independent of search, dashboard, or tagging features.

**Acceptance Scenarios**:

1. **Given** a user is viewing a knowledge base, **When** they create a folder, **Then** the folder appears in the knowledge base's tree view and can immediately hold documents or subfolders.
2. **Given** a folder exists, **When** the user creates a subfolder inside it, **Then** the nested structure is reflected in the tree view up to the configured nesting depth.
3. **Given** a user is viewing the folder tree, **When** they drag a document (or folder) into a different folder, **Then** the item moves and the tree updates without a page reload.
4. **Given** a user attempts to move a folder into one of its own descendant folders, **When** they perform the action, **Then** the system rejects the move to prevent a circular structure.
5. **Given** a folder still contains documents or subfolders, **When** a user deletes it, **Then** the system asks the user to confirm and explains what will happen to the folder's contents.

---

### User Story 3 - Archive and restore a knowledge base (Priority: P2)

A user archives a knowledge base that is no longer in active use (e.g., a completed project) to get it out of their day-to-day view without losing it, and can later restore it to active status if it becomes relevant again.

**Why this priority**: Archiving is the mechanism that keeps the dashboard usable as a user accumulates knowledge bases over time, and it is called out explicitly as a distinct lifecycle state from delete. It depends on Story 1 (a knowledge base must exist) but not on folders, tags, or search.

**Independent Test**: Can be fully tested by archiving an active knowledge base, confirming it disappears from the default active list and appears in an "Archived" view, then restoring it and confirming it returns to "Active".

**Acceptance Scenarios**:

1. **Given** an active knowledge base, **When** the user archives it, **Then** its status changes to "Archived" and it no longer appears in the default active list.
2. **Given** an archived knowledge base, **When** the user views the Archived filter on the dashboard, **Then** the knowledge base is listed there with an option to restore it.
3. **Given** an archived knowledge base, **When** the user restores it, **Then** its status returns to "Active" and it reappears in the default active list.
4. **Given** a knowledge base is archived, **When** any future RAG indexing process would run (out of scope for this spec, but its eligibility is defined here), **Then** the knowledge base is marked ineligible for indexing while archived.

---

### User Story 4 - Discover knowledge bases through the dashboard (Priority: P2)

A user with many knowledge bases opens the Knowledge Base dashboard and uses search, filters, sorting, grid/list view toggle, favorites, and pinning to quickly find the one they need.

**Why this priority**: As the number of knowledge bases grows, discovery becomes essential to the feature remaining usable, but the dashboard is a view over data created in Stories 1–3, so it is ordered after core lifecycle management.

**Independent Test**: Can be fully tested by creating several knowledge bases with different names/categories/tags, then using the search bar, filters, and sort options to narrow the list, and marking one as a favorite and another as pinned to confirm they surface in their respective dashboard sections.

**Acceptance Scenarios**:

1. **Given** a user has multiple knowledge bases, **When** they type a search term matching a name, description, or tag, **Then** only matching knowledge bases are shown, updating as they type.
2. **Given** a user applies a filter (status, category, owner, or tag), **When** the filter is active, **Then** only knowledge bases matching all active filters are displayed, and the applied filters are visibly indicated.
3. **Given** a user selects a sort option (name, recently updated, created date, document count, storage size), **When** the sort is applied, **Then** the list re-orders accordingly.
4. **Given** a user toggles between grid and list view, **When** the toggle changes, **Then** the same result set is redisplayed in the selected layout without losing the current search/filter/sort state.
5. **Given** a user marks a knowledge base as a favorite or pins it, **When** they return to the dashboard, **Then** it appears in the "Favorites" or "Pinned" section respectively.
6. **Given** a search or filter combination matches nothing, **When** the results render, **Then** the user sees a clear empty state rather than a blank screen.

---

### User Story 5 - Classify knowledge bases with categories and tags (Priority: P3)

A user assigns a category (e.g., "Engineering") and one or more free-form tags to a knowledge base so it can be classified consistently and found later through category or tag filters.

**Why this priority**: Classification enhances discovery and future retrieval quality but a knowledge base is fully functional without it; it builds on Stories 1 and 4.

**Independent Test**: Can be fully tested by assigning a category and adding multiple tags to a knowledge base, then filtering the dashboard by that category and by one of the tags and confirming the knowledge base appears in both result sets.

**Acceptance Scenarios**:

1. **Given** a user is editing a knowledge base, **When** they assign one of the predefined categories, **Then** the category is saved and shown on the knowledge base's card/row.
2. **Given** a user is editing a knowledge base, **When** they create and assign a custom category, **Then** the new category becomes available for future use and is saved on the knowledge base.
3. **Given** a user is editing a knowledge base, **When** they add one or more tags, **Then** the tags are saved and displayed, and each tag becomes available as a filter option.
4. **Given** a knowledge base has a category and tags, **When** the user removes one, **Then** the removal is saved and the knowledge base no longer matches filters for the removed value.

---

### User Story 6 - Duplicate and export a knowledge base (Priority: P3)

A user duplicates an existing knowledge base to quickly start a similar one without rebuilding its structure from scratch, and exports a knowledge base's metadata for record-keeping or migration purposes.

**Why this priority**: These are productivity accelerators on top of an already-functional system; valuable but not required for the feature to deliver its core promise.

**Independent Test**: Can be fully tested by duplicating a knowledge base and confirming a new, independent knowledge base is created with copied metadata and structure, and by exporting a knowledge base and confirming a downloadable metadata file is produced.

**Acceptance Scenarios**:

1. **Given** a user owns a knowledge base, **When** they duplicate it, **Then** a new knowledge base is created with a distinguishing name (e.g., "Copy of X"), owned by the same user, independent of the original (later edits to one do not affect the other).
2. **Given** a user owns a knowledge base, **When** they export its metadata, **Then** the system produces a downloadable file containing the knowledge base's metadata, statistics, folder structure, categories, and tags.

---

### Edge Cases

- What happens when a user tries to permanently delete a knowledge base that has not yet been soft-deleted? The system MUST require it to be soft-deleted (and confirmed) first.
- How does the system handle two knowledge bases with the same name owned by the same user? Duplicate names are permitted but the dashboard MUST disambiguate them (e.g., by creation date) since name alone is not a unique identifier.
- What happens when a user attempts to nest folders beyond the configured depth limit? The system MUST block the action and explain the limit.
- How does the system handle a search or filter combination that returns zero results? The dashboard MUST show an explicit empty state, not a blank or ambiguous screen.
- What happens when a knowledge base is archived while it is marked as a favorite or pinned? It MUST retain its favorite/pinned marker but be excluded from the default active list, surfacing only under Archived (and Favorites/Pinned views, clearly labeled as archived).
- What happens when two sessions edit the same knowledge base's metadata concurrently? The later save MUST NOT silently overwrite the earlier one without the user being made aware (e.g., a conflict/refresh notice), consistent with the "no silent failures" requirement.
- What happens when a user deletes a category or tag that is still assigned to knowledge bases? Knowledge bases MUST retain a sensible fallback (e.g., "Uncategorized") rather than entering a broken/invalid state.
- What happens when a folder or document move operation fails partway through? The system MUST report the failure to the user and leave the structure in its last-known-consistent state rather than a partial move.
- What happens when a user restores a soft-deleted knowledge base before its 30-day retention window elapses? The system MUST cancel the pending automatic purge and return the knowledge base to its prior status (Active or Archived).
- What happens when the 30-day retention window elapses while a user is actively viewing the soft-deleted knowledge base? The automatic purge MUST still proceed, and any subsequent access attempt MUST return a clear "no longer available" outcome rather than a generic error.
- What happens to a knowledge base's document files when it is permanently purged? The purge MUST cascade to permanently delete those files from storage, since a document has no owner once its sole knowledge base is gone; the deletion MUST be logged in the audit trail before the files are removed.

## Requirements *(mandatory)*

### Functional Requirements

**Lifecycle & CRUD**

- **FR-001**: Users MUST be able to create an unlimited number of knowledge bases, each with at minimum a required name.
- **FR-002**: System MUST assign every new knowledge base an initial status of "Draft" and support transitions between Draft, Active, Archived, and Deleted (soft-deleted) states.
- **FR-003**: Users MUST be able to edit a knowledge base's name, description, color, icon, category, tags, and notes at any time while it is not deleted.
- **FR-004**: Users MUST be able to archive an Active knowledge base and restore an Archived knowledge base back to Active.
- **FR-005**: Users MUST be able to delete a knowledge base, which MUST perform a soft delete (marks it deleted and hides it from normal views) rather than immediately erasing its data.
- **FR-006**: Only Active knowledge bases are eligible to participate in future RAG indexing; Draft, Archived, and Deleted knowledge bases MUST be excluded from that eligibility.
- **FR-007**: System MUST validate all knowledge base input (e.g., required name, maximum field lengths) and surface validation failures to the user with actionable messages, never as a silent no-op.
- **FR-008**: System MUST record `Created Date` and `Last Updated` timestamps automatically and MUST NOT allow users to set them directly.

**Ownership & Security**

- **FR-009**: Every knowledge base MUST have exactly one owner, set to the creating user, and all knowledge bases are private to their owner in this release (no team/organization/public sharing).
- **FR-010**: Users MUST only be able to view, edit, archive, restore, delete, or duplicate knowledge bases they own; attempts to act on another user's knowledge base MUST be denied and MUST NOT reveal whether the target exists if the acting user has no access to it.
- **FR-011**: System MUST record an audit log entry for every create, edit, archive, restore, delete, permanent-delete, and duplicate action, capturing who performed it, when, and what changed.

**Organization (Folders)**

- **FR-012**: Users MUST be able to create folders inside a knowledge base and subfolders inside existing folders, up to a configurable maximum nesting depth.
- **FR-013**: System MUST prevent a folder from being moved or nested into itself or any of its own descendants.
- **FR-014**: Users MUST be able to move documents and folders between folders within the same knowledge base, including via drag-and-drop in the UI.
- **FR-015**: System MUST require explicit confirmation before deleting a folder that still contains documents or subfolders, and MUST inform the user what happens to that content.
- **FR-016**: Each document MUST belong to exactly one knowledge base (and at most one folder within it) at a time; a document MUST NOT be simultaneously owned by multiple knowledge bases in this release.

**Categories & Tags**

- **FR-017**: System MUST provide a predefined set of default categories (Engineering, Architecture, Construction, Legal, Finance, Research, Education, General) available to every user.
- **FR-018**: Users MUST be able to create custom categories in addition to the predefined set; custom categories are private to their creator (see FR-038).
- **FR-019**: Users MUST be able to assign exactly one category and any number of free-form tags to a knowledge base.
- **FR-020**: Tags and categories MUST be usable as both search terms and dashboard filters.
- **FR-021**: System MUST handle removal of a category or tag still in use by falling back existing assignments to an "Uncategorized"/untagged state rather than leaving an invalid reference.

**Discovery (Search, Filter, Sort)**

- **FR-022**: Users MUST be able to search their knowledge bases by name, description, and tags. Creation date and last-updated date are available via sorting (FR-024), not free-text search.
- **FR-023**: Users MUST be able to filter their knowledge base list by status, category, tag, and favorite/pinned state, with multiple filters combinable. (Owner is not a filter dimension in this release — every knowledge base is already scoped to exactly one owner, the caller, per FR-009.)
- **FR-024**: Users MUST be able to sort their knowledge base list by name (alphabetical), recently updated, created date, document count, and storage size, in ascending or descending order.
- **FR-025**: Search and filter results MUST update to reflect the current query/filter state without requiring a full page reload.

**Dashboard**

- **FR-026**: System MUST provide a dedicated Knowledge Base dashboard offering both grid and list layouts for the same underlying result set.
- **FR-027**: Dashboard MUST provide dedicated views/sections for Recent, Favorites, Pinned, and Archived knowledge bases.
- **FR-028**: Users MUST be able to mark any owned knowledge base as a favorite and/or pin it, and unmark it, independent of its lifecycle status.
- **FR-029**: Dashboard MUST display summary statistics cards (e.g., total knowledge bases, total documents, total storage used) reflecting the user's current knowledge bases.

**Statistics**

- **FR-030**: System MUST track and display, per knowledge base: number of documents, number of pages (where applicable to the document type), storage size, processing status, and last-updated date.
- **FR-031**: Statistics MUST update to reflect the current state of a knowledge base's documents after any change that affects them (add/remove/move a document).

**Duplication & Export**

- **FR-032**: Users MUST be able to duplicate a knowledge base, producing a new, independently editable knowledge base that does not affect the original when later modified (copy depth defined in FR-037).
- **FR-033**: Users MUST be able to export a knowledge base's metadata (name, description, category, tags, folder structure, statistics, notes) as a downloadable file suitable for later re-import.

**Performance & Scale**

- **FR-034**: System MUST support pagination for all list and search endpoints so that dashboards remain responsive as a user's knowledge base count grows into the thousands.
- **FR-035**: System MUST cache dashboard summary statistics rather than recomputing full aggregates on every dashboard load.

**Permanent Deletion, Duplication Depth & Category Scope**

- **FR-036**: System MUST automatically and permanently purge a knowledge base 30 days after it was soft-deleted, and MUST also allow the owner to request immediate permanent deletion before that window elapses, with explicit confirmation required in both the automatic-window notice and the immediate path. Permanently purging a knowledge base MUST cascade to permanently delete its associated document files from storage (via the File Management Engine), since a document belongs to exactly one knowledge base and has no other owner once that knowledge base is gone.
- **FR-037**: When a user duplicates a knowledge base, the system MUST deep-copy its folder structure into a new, fully independent knowledge base that is immediately usable without the user re-adding documents. Each document MUST be duplicated as an independent physical file copy (not a shared reference to the original), so that permanently purging either the original or the duplicate never affects the other.
- **FR-038**: Custom categories created by a user MUST be private to that user — visible and usable only in their own category list and filters — while the predefined default categories remain shared and visible platform-wide.

**Accessibility**

- **FR-039**: All Knowledge Base workspace screens (dashboard, tree/folder view, create/edit forms, context menus, statistics cards) MUST conform to WCAG 2.2 Level AA.
- **FR-040**: Every interactive workflow (create, rename, archive, restore, delete, search, filter, sort, favorite/pin, and organizing documents/folders) MUST be fully operable using the keyboard alone; drag-and-drop organization MUST have a keyboard-accessible equivalent (e.g., a "Move to folder" action).
- **FR-041**: All non-text UI elements (icons, color-coded status/category indicators, statistics cards) MUST have text alternatives usable by screen readers, and color MUST NOT be the sole means of conveying status, category, or any other information.
- **FR-042**: The workspace MUST support a high-contrast display mode and MUST remain fully usable and legible across responsive breakpoints from mobile through desktop widths.

### Key Entities

- **KnowledgeBase**: A private, user-owned container grouping related documents for a purpose. Attributes: name, description, owner, status (Draft/Active/Archived/Deleted), visibility (Private only in this release), color, icon, category, tags, notes, favorite flag, pinned flag, created date, last-updated date. Owns folders and (indirectly, via folders) documents.
- **KnowledgeBaseFolder**: A named node within a knowledge base's hierarchy that can contain documents and other folders (subfolders), up to a configured maximum depth. Belongs to exactly one knowledge base and at most one parent folder.
- **KnowledgeBaseTag**: A free-form, reusable label that can be assigned to any number of knowledge bases, usable for search and filtering.
- **KnowledgeBaseCategory**: A classification value (predefined or user-created) assignable one-per-knowledge-base, usable for search and filtering.
- **KnowledgeBaseStatistics**: A computed/cached summary (document count, page count, storage size, processing status) associated with a single knowledge base, refreshed when the knowledge base's document contents change.
- **KnowledgeBasePermission**: A future-ready association between a knowledge base and a user or group defining an access role; not exercised in this release (all knowledge bases are private to their owner) but present in the schema so team/organization sharing can be added without a breaking change.
- **KnowledgeBaseAuditLog**: An immutable record of a lifecycle or ownership-relevant action taken on a knowledge base (who, what, when, and what changed).
- **Document** *(reference only)*: A file already tracked by the platform's existing file-management capability. This spec governs a document's association with exactly one knowledge base/folder, its contribution to that knowledge base's statistics, and triggers permanent deletion of the underlying file when its owning knowledge base is permanently purged (FR-036). It does not govern file upload, storage mechanics, text extraction, or content processing, which remain the responsibility of the existing file-management capability and the future RAG pipeline spec.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user can create their first knowledge base and see it appear in their dashboard in under 30 seconds from opening the workspace.
- **SC-002**: Users can locate a specific knowledge base via search or filters in under 10 seconds, with 95% of searches returning matching results in under 1 second.
- **SC-003**: The dashboard remains fully responsive (list, search, filter, and sort operations complete in under 2 seconds) for a user with at least 1,000 knowledge bases.
- **SC-004**: 100% of *irreversible* destructive actions (permanent delete, folder deletion with contents) require an explicit, unambiguous user confirmation before taking effect. Plain (reversible, soft) delete intentionally requires no confirmation, consistent with the existing Chat Engine's convention — it can be undone via Restore.
- **SC-005**: Users can archive a knowledge base and later restore it, with the restored knowledge base's structure, metadata, and statistics identical to their state immediately before archiving, 100% of the time.
- **SC-006**: Users can duplicate a knowledge base and begin working in the new copy in under 10 seconds for knowledge bases with up to 1,000 documents.
- **SC-007**: The platform sustains at least 10,000 knowledge bases per user and 1,000,000 documents platform-wide without a measurable increase in dashboard load time.
- **SC-008**: Zero cross-user data exposure incidents: a user attempting to access another user's knowledge base by ID or URL is denied 100% of the time.
- **SC-009**: 100% of soft-deleted knowledge bases are either restored or permanently purged within 30 days of their soft-delete date, with no soft-deleted knowledge base persisting indefinitely in a recoverable state.
- **SC-010**: The Knowledge Base workspace passes an automated WCAG 2.2 AA accessibility audit with zero critical or serious violations, and 100% of primary flows (create, organize, search, archive) are completable using keyboard-only navigation.

## Assumptions

- Document upload, storage, sanitization, and text extraction are handled by the platform's existing file-management capability; this specification covers only how documents are organized into knowledge bases/folders and reflected in statistics, not the upload or extraction mechanics themselves.
- "Number of Pages" is only meaningful for paginated document types (PDF, Word, PowerPoint); other supported types (e.g., CSV, Markdown) display "Not Applicable" for that statistic rather than an error or a fabricated value.
- The `Visibility` metadata field is included in the data model now (to avoid a future breaking schema change) but is constrained to the single value "Private" until team/organization/public sharing ships in a later specification.
- Folder nesting depth is configurable per deployment/knowledge base, with a system-wide default maximum of 10 levels — deep enough to satisfy "future deep hierarchies" without permitting unbounded recursion or pathological tree sizes.
- Exported metadata is produced in a structured, machine-readable format (e.g., JSON) so that the "import metadata" capability referenced as a future release can consume it without a format change.
- "Recently Updated" and similar relative-time dashboard sections use the same recency window conventions as other list views already in the product (e.g., Chat history), for consistency rather than introducing a new convention.
- Knowledge base names are not required to be globally or even per-user unique; the dashboard disambiguates same-named knowledge bases using creation date and/or owner, consistent with how conversations are already handled in the Chat Engine.
