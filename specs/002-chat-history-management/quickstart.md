# Quickstart: Validating Chat History & Conversation Management

**Feature**: [spec.md](./spec.md) | **Contracts**: [contracts/chats-api.md](./contracts/chats-api.md)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the
spec's user stories and success criteria. Run after implementation, before marking the
feature done (constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`, which hosts both
  the API and the built `ClientApp`), against a local SQL Server instance with the
  feature's migrations applied (`dotnet ef database update`).
- A logged-in test user (existing auth flow — see `docs/TESTING.md`).
- Two browser sessions/tabs (or one browser + one API client) for the multi-device
  scenarios.

## Scenario 1 — Persistence across sessions (User Story 1 / SC-002)

1. Create 3 new conversations; send at least one message in each.
2. Sign out, sign back in (or reload the app).
3. Confirm all 3 conversations are listed and each opens with its full message history,
   in the original order.

**Pass condition**: no conversation or message is missing, reordered, or altered.

## Scenario 2 — Search, filter, sort, pagination (User Story 2 / SC-001, SC-001a)

1. Seed (via script or repeated UI use) enough conversations to exceed one page
   (`pageSize` from contracts/chats-api.md).
2. Scroll the sidebar; confirm additional conversations load progressively
   (`GET /api/v1/chats?cursor=...`) without a full page reload.
3. Send a message containing a distinctive token (e.g., `zzqconveyor`); within a few
   seconds, search for that token via `?q=zzqconveyor` and confirm the conversation is
   returned.
4. Apply `sort=alphabetical`, `sort=recently-updated`; confirm ordering changes as
   expected. Apply `pinned=true`/`favorite=true` filters (after Scenario 3) and confirm
   only matching conversations appear.

**Pass condition**: search finds the just-sent message within a few seconds; sort/filter
results match the applied parameter; scrolling never blocks on a full-list fetch.

## Scenario 3 — Pin, favorite, archive, restore, duplicate, clear (User Story 3)

1. Pin a conversation → confirm it moves to the top of the list regardless of recency.
   Unpin → confirm it returns to normal chronological position.
2. Favorite a conversation → confirm it appears under `favorite=true`.
3. Archive a conversation → confirm it disappears from the default (`view=active`) list
   and appears under `view=archived`.
4. Restore it (`POST .../actions/restore`) → confirm it's back under `view=active` with
   its prior pin/favorite state intact.
5. Duplicate a conversation with ≥2 messages → confirm a new conversation appears
   containing a full copy of those messages, and the original is unchanged.
6. Clear a conversation's messages (confirming the prompt) → confirm the conversation
   remains in the list with its title, but has zero messages.

**Pass condition**: each action's effect matches its acceptance scenario in spec.md
User Story 3, and confirmation is required before Clear takes effect.

## Scenario 4 — Delete, Recently Deleted, and permanent delete (User Story 4 / SC-008)

1. Delete a conversation (`DELETE /api/v1/chats/{id}`) → confirm it disappears from
   `view=active` and appears under `view=deleted`.
2. Restore it from Recently Deleted → confirm it reappears in the default list with
   prior archive/pin/favorite state intact.
3. Delete it again, then attempt `POST /api/v1/chats/{id}/actions/purge` **without**
   `confirm: true` → confirm the request is rejected (`400`) and nothing is deleted.
4. Repeat with `{ "confirm": true }` → confirm the conversation is gone from every view
   (`active`, `archived`, `deleted`) and from search/export.
5. As a second user, attempt to delete/purge the first user's conversation id directly
   → confirm `404`.

**Pass condition**: permanent delete never succeeds without explicit confirmation
(SC-008); a purged conversation is unrecoverable through any listed surface.

## Scenario 5 — Export (User Story 5 / SC-007)

1. Export a conversation containing text messages, at least one attachment, and at
   least one citation → confirm the downloaded file contains the full ordered message
   history, the conversation's title/dates, and attachment/citation entries as
   references (filename/type/access location), not embedded file bytes.
2. Export a conversation with zero messages → confirm a valid file with an empty
   `messages` array is returned, not an error.

**Pass condition**: exported content round-trips losslessly against what's shown in the
UI for that conversation.

## Scenario 6 — Automatic and manual titles (User Story 6 / SC-004)

1. Start a new conversation, send a first message, and (without manually naming it)
   confirm a descriptive title appears within ~1 second.
2. Manually rename the conversation → send additional messages → confirm the manual
   title is never overwritten by auto-generation afterward.
3. Confirm the same title is shown in the sidebar, the conversation header, and export.

**Pass condition**: auto-title appears near-instantly and only while no manual title has
been set; manual titles are permanently sticky.

## Scenario 7 — Cross-cutting: ownership, concurrency, streaming edge case

1. As User A, attempt every action above (`GET`/mutations) against a conversation id
   owned by User B → confirm `404` for all of them, and confirm the attempt(s) are
   logged as a security event (FR-028).
2. Open the same conversation in two tabs; rename it in tab 1, then attempt to archive
   it in tab 2 using the stale `RowVersion` → confirm a `409 Conflict` is surfaced to the
   user, not a silent overwrite or unhandled error (constitution §2.VIII, No Silent
   Failures).
3. While an assistant response is actively streaming into a conversation, attempt to
   archive/delete/clear it → confirm the in-progress message is not corrupted (either
   the action is blocked until the stream completes, or the partial content is safely
   finalized first).

**Pass condition**: every cross-user attempt is denied and logged; every failure path
(concurrency conflict, mid-stream action) is visibly surfaced to the user, never silent.
