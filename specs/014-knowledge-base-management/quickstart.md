# Quickstart: Validating Knowledge Base Management

**Feature**: [spec.md](./spec.md) | **Data model**: [data-model.md](./data-model.md) |
**Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the
spec's user stories and success criteria. Run after implementation, before marking the
feature done (constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`), against a local SQL
  Server instance with this feature's migration (`AddKnowledgeBaseManagement`) applied and
  the 8 predefined categories seeded.
- A logged-in test user, and a small set of sample files covering the supported document
  types (PDF, `.docx`, `.xlsx`, `.pptx`, `.md`, `.csv`, `.txt`) plus one deliberately
  mislabeled file (e.g., a `.txt` renamed to `.pdf`) for the content-validation scenario.
- Ability to fast-forward the 30-day purge window in a test environment (e.g., an
  injectable/overridable clock or directly adjusting `PurgeScheduledAtUtc` in the test
  database) — this feature cannot be meaningfully validated by waiting 30 real days.

## Scenario 1 — Core lifecycle: create, edit, delete (User Story 1 / SC-001, SC-004)

1. Create a knowledge base with just a name. Confirm it appears in the dashboard immediately
   with `status: Draft` (FR-001/FR-002).
2. Edit its name, description, color, and icon. Confirm the changes are reflected on the
   dashboard card and its `Last Updated` timestamp changes (FR-003).
3. Try creating a knowledge base with a blank name — confirm a clear validation error, not a
   silent failure or a KB created anyway (FR-007).
4. Delete the knowledge base. Confirm it disappears from the default (Active) view but is
   still visible under the Deleted/Trash view (FR-005) — this is a soft delete, not
   immediately gone.
5. Time the round trip from opening the dashboard to a newly created knowledge base being
   visible — should be under 30 seconds (SC-001).

**Pass condition**: matches spec.md User Story 1's five acceptance scenarios.

## Scenario 2 — Folder organization, including drag-and-drop and depth limits (User Story 2 / FR-012–FR-016)

1. Activate the knowledge base (`POST .../actions/activate`, research.md Decision 1) so it's
   `Active`. Create a folder, then a subfolder inside it. Confirm the tree view reflects the
   nesting.
2. Upload a document to the root and one into the subfolder. Confirm both appear in the
   correct place with the right `fileName`/`sizeBytes`, and (for the PDF/DOCX/PPTX samples)
   a non-null `pageCount` (research.md Decision 5) — confirm the `.txt`/`.csv`/`.md` samples
   show `pageCount: null`/"N/A" instead of an error.
3. Upload the mislabeled file (`.txt` renamed to `.pdf`). Confirm the upload is rejected with
   a specific, actionable message identifying the mismatch (research.md Decision 8) — not a
   generic error, not a silent accept.
4. Drag a document from the root into the subfolder via the UI; confirm it moves without a
   page reload (FR-014). Repeat the same move using only the keyboard (no pointer input) and
   confirm it succeeds identically (FR-040).
5. Attempt to nest folders past the configured max depth (10 by default) — confirm the system
   blocks the action and explains the limit (FR-012, spec.md Edge Cases).
6. Attempt to move a parent folder into its own child folder — confirm the system rejects it
   with an explanation (FR-013).
7. Delete a folder that still contains a document — confirm the system requires explicit
   confirmation and states what's inside before proceeding (FR-015).

**Pass condition**: matches User Story 2's five acceptance scenarios plus the depth-limit and
circular-move edge cases.

## Scenario 3 — Archive and restore (User Story 3 / SC-005)

1. Archive the Active knowledge base from Scenario 2. Confirm it disappears from the default
   Active view and appears under Archived (FR-004).
2. Restore it. Confirm it returns to Active, and that its folder structure, documents, tags,
   and category are byte-for-byte unchanged from immediately before archiving (SC-005).
3. Mark the knowledge base as a favorite, then archive it again. Confirm it still appears in
   the Favorites view, clearly labeled as archived (spec.md Edge Cases).

**Pass condition**: matches User Story 3's four acceptance scenarios and SC-005.

## Scenario 4 — Dashboard discovery: search, filter, sort, grid/list, favorites, pinned (User Story 4 / SC-002, SC-003)

1. Create 3–5 more knowledge bases with varied names, categories, and tags.
2. Search by a substring of one knowledge base's name; confirm only matches appear, updating
   as you type (FR-022/FR-025).
3. Filter by category, then by a tag, then combine both filters; confirm the result set
   reflects all active filters (FR-023).
4. Sort by name, then by recently updated, then by document count; confirm re-ordering each
   time (FR-024).
5. Toggle grid/list view; confirm the same filtered/sorted result set is preserved across the
   toggle (FR-026).
6. Favorite one knowledge base and pin another; confirm each appears in its respective
   dashboard section (FR-027/FR-028).
7. Search for something that matches nothing; confirm a clear empty state, not a blank screen
   (spec.md Edge Cases).
8. Time a search/filter round trip — should be under 1 second for 95% of queries (SC-002); at
   scale (seed ~1,000 knowledge bases via a test script if available), confirm list/search/
   sort operations stay under 2 seconds (SC-003).

**Pass condition**: matches User Story 4's six acceptance scenarios and SC-002/SC-003.

## Scenario 5 — Categories and tags, including private-custom-category scoping (User Story 5 / FR-017–FR-021, FR-038)

1. Assign a predefined category (e.g., "Engineering") to a knowledge base; confirm it's shown
   on the card and is filterable.
2. Create a custom category ("Vendor Docs") and assign it; confirm it's saved and usable as a
   filter (FR-018/FR-019).
3. Log in as a **second** user; confirm "Vendor Docs" does **not** appear in that user's
   category list (FR-038 — private custom category, verified via
   `GET /api/v1/knowledge-bases/categories`).
4. Add two tags to a knowledge base; confirm both appear and each becomes usable as a filter
   (FR-020).
5. Delete the custom category "Vendor Docs" (as its owner). Confirm the knowledge base that
   referenced it now shows "Uncategorized" rather than an error or a dangling reference
   (FR-021).

**Pass condition**: matches User Story 5's four acceptance scenarios plus the cross-user
category-privacy check.

## Scenario 6 — Duplication and export, including independent file copies (User Story 6 / SC-006)

1. Duplicate a knowledge base that has a folder structure and several documents. Confirm a
   new, independent knowledge base appears (`"Copy of {name}"`, its own id, `status: Draft`)
   within 10 seconds (SC-006).
2. Edit a document's containing folder in the **duplicate**; confirm the **original** is
   unaffected (FR-032).
3. Permanently purge the **duplicate** (Scenario 7 below) and confirm the **original**'s
   documents are still fully intact and openable afterward — proving the physical file copies
   were independent, not shared references (research.md Decision 4, spec.md Clarifications).
4. Export the original knowledge base's metadata; confirm the downloaded JSON contains name,
   description, category, tags, folder structure, statistics, and notes (FR-033).

**Pass condition**: matches User Story 6's two acceptance scenarios, SC-006, and the
independent-copy clarification.

## Scenario 7 — Permanent deletion: owner-triggered and automatic 30-day sweep (FR-036, SC-009)

1. Soft-delete a knowledge base. Attempt to permanently purge it **without** `confirm: true`
   — confirm the request is rejected (FR-036).
2. Permanently purge it with `confirm: true`. Confirm: it disappears entirely (including from
   the Deleted view), its documents' underlying files are gone from storage (verify on disk
   for `LocalFileStorage`), and a `KnowledgeBaseAuditLog` entry (`PermanentlyDeleted`) exists.
3. Soft-delete a second knowledge base. Restore it **before** purging. Confirm the pending
   purge is cancelled — advancing the fast-forwarded clock past the original 30-day mark must
   **not** purge it (spec.md Edge Cases).
4. Soft-delete a third knowledge base and do **not** restore it. Fast-forward the purge sweep
   past its `PurgeScheduledAtUtc`. Confirm the `KnowledgeBasePurgeHostedService` purges it
   automatically, with the same cascade-file-deletion and audit-log guarantees as step 2
   (SC-009).
5. Attempt to permanently purge a knowledge base that is **not** currently soft-deleted;
   confirm it's rejected (spec.md Edge Cases — "must be soft-deleted first").

**Pass condition**: matches FR-036 and SC-009, including the restore-cancels-purge and
purge-requires-prior-soft-delete edge cases.

## Scenario 8 — Accessibility (FR-039–FR-042, SC-010)

1. Run an automated WCAG 2.2 AA audit (e.g., axe) against the dashboard, folder tree, and
   create/edit dialogs; confirm zero critical/serious violations (SC-010).
2. Complete Scenario 1 (create/edit/delete) and Scenario 2's move operation using only the
   keyboard, no pointer input at any step; confirm every action remains reachable and
   operable (FR-040).
3. Confirm status/category indicators are distinguishable without relying on color alone
   (e.g., an icon or text label accompanies each color-coded state) (FR-041).
4. Switch the OS/browser to a high-contrast mode; confirm the workspace remains legible and
   usable (FR-042). Resize the viewport down to a mobile width; confirm the layout adapts
   without broken/overlapping elements (FR-042).

**Pass condition**: matches FR-039–FR-042 and SC-010.
