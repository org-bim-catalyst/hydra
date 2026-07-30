# Phase 0 Research: Chat Loading & Reply Feedback Fixes

No items in the Technical Context were marked `NEEDS CLARIFICATION` — this is a bug-fix
feature against an existing, already-understood codebase, and the four unknowns raised
during `/speckit-clarify` (latency threshold, retry mechanism, reduced-motion handling,
minimum flash duration) were resolved there and are encoded directly in the spec's
Functional Requirements. The research below instead confirms the concrete mechanism each
fix will use against the actual current implementation, so Phase 1 design can proceed
without re-deriving root cause during `/speckit-tasks` or implementation.

## Topic 1: Why the empty-state placeholder can show while a conversation is loading

**Decision**: Read `useChatMessages(chatId)`'s own `isPending`/`isError`/`error`/`refetch`
(from `useInfiniteQuery`, already returned by the hook's underlying `UseInfiniteQueryResult`
but not currently destructured in `ConversationView`) and branch render output on
`chatId === null` (empty state) vs. `isPending` (loading) vs. `isError` (error+retry) vs.
loaded content — instead of the current `messages.length === 0` check.

**Rationale**: `ChatPage.tsx`'s `ConversationView` (lines 97-116) calls
`useChatMessages(chatId)`, memoizes `persistedMessages` from `data`, and passes it as
`initialMessages` into `useChatStream`. `useChatStream` seeds its `messages` state from
`initialMessages ? toChatMessages(initialMessages) : []` — so for the entire duration
between mount and the query's first resolution, `messages` is `[]`, and the JSX's
`messages.length === 0` check (line 192) renders "Start a conversation with Ask Lucy."
regardless of whether a conversation is selected and loading, or none is selected at all.
This is the exact root cause of spec Bug #1/#3: the two states are indistinguishable today.
Because `ConversationView` is fully remounted (via the parent's `key={viewKey}`) on every
explicit conversation switch, there is no risk of a previous conversation's stale content
lingering — the bug is a missing loading/error branch, not a stale-data bug.

**Alternatives considered**:
- *Keep deriving loading state from `messages.length` but add a separate `isNewChat`
  boolean.* Rejected — duplicates state TanStack Query already tracks accurately and
  reintroduces the same class of bug if the two ever drift.
- *Debounce the empty-state render by a fixed delay so fast fetches never show a flash.*
  Rejected by the spec clarification (no minimum display duration) and unnecessary once the
  real `isPending` flag is used, since `isPending` is `true` synchronously from mount.

## Topic 2: Race safety when rapidly switching conversations

**Decision**: No additional guarding code is required beyond what already exists.

**Rationale**: `useChatMessages` keys its query as `['chats', chatId, 'messages']`, and
`ConversationView` is remounted via `key={viewKey}` (bumped on every `onSelectChat`/`onNewChat`
call) — React unmounts the previous `ConversationView` (and with it, the previous
`useChatMessages`/`useChatStream` hook instances) before mounting the new one. TanStack
Query scopes cache entries per query key, so an in-flight fetch for a since-abandoned
`chatId` resolves into its own cache entry, never into whatever is currently rendered.
Spec FR-005 is therefore already satisfied structurally; Phase 1 design does not need a new
cancellation/staleness mechanism, only the render-branch fix in Topic 1.

## Topic 3: Detecting "first content arrived" for the thinking indicator

**Decision**: Show the `ThinkingIndicator` in place of a message bubble whenever
`isStreaming` is `true` and that message's `content === ''`; replace it with normal
`MessageBubble` rendering the instant `content` becomes non-empty.

**Rationale**: `useChatStream.send` (lines 80-121) already inserts a placeholder assistant
message (`{ role: 'assistant', content: '' }`) synchronously before awaiting
`ensureChatId`/`streamChat`, then updates that same message's `content` incrementally as
each `streamChat` chunk (an `AsyncGenerator<string>` over SSE, `aiApi.ts`) arrives. No new
plumbing is needed to detect "no content yet" — it is already the initial state of that
exact message object; the indicator is a purely presentational branch in whichever
component renders a message list item (`ConversationView`'s virtualized row, deciding
`ThinkingIndicator` vs. `MessageBubble` per item).

**Alternatives considered**:
- *Add a separate `isThinking` boolean to `useChatStream`'s return value.* Considered
  equally valid, but rejected in favor of deriving straight from `content === ''` +
  `isStreaming` to avoid a second piece of state that must be kept in sync with the message
  array — one less invariant to maintain.

## Topic 4: Surfacing a retry action for a failed send (Bug #2's error path, FR-008)

**Decision**: `useChatStream` already catches `streamChat`/`ensureChatId` failures (lines
108-114), drops the placeholder bubble, and sets a global `error` string shown via the
existing page-level `Snackbar` (`ChatPage.tsx` lines 214-218). Phase 1 design adds a `retry`
callback to `useChatStream`'s return value that re-invokes `send` with the same content that
just failed (stored in a ref at the top of `send`), and the `Snackbar`'s `Alert` gains an
action button wired to it — no separate inline-in-bubble retry surface is needed, since the
existing Snackbar is already the app's established pattern for this kind of transient,
retryable failure (`ChatSidebar.tsx` uses the identical Snackbar+`actionError` pattern for
its own mutation failures).

**Rationale**: Reuses an established, already-accessible UI pattern rather than inventing a
second one; keeps `MessageBubble`/`ThinkingIndicator` free of retry-button responsibility,
which belongs with the `send` call that owns the retryable content.

**Alternatives considered**:
- *Inline "Retry" button rendered where the thinking indicator was.* Rejected — would
  require `MessageBubble`/the list-rendering code to know about resend semantics, coupling
  presentation to `useChatStream`'s retry mechanics for no behavioral benefit over the
  existing Snackbar pattern, which already satisfies FR-008's "visible, user-facing error
  state with a manual Retry action."

## Topic 5: Retry action for a failed conversation-messages load (FR-004)

**Decision**: Use `useInfiniteQuery`'s own `refetch()` (returned alongside `isPending`/
`isError`, per Topic 1) as the "Retry" button's handler in the new error-state branch.

**Rationale**: TanStack Query already provides exactly this affordance; no custom retry
logic is needed. This mirrors Topic 4's principle of using library/pattern-provided retry
mechanisms rather than hand-rolling one.

**Alternatives considered**: None — this is the standard, idiomatic TanStack Query
mechanism for the exact scenario (re-run a failed query on user request).

## Topic 6: Root cause of the "new chat blank on return" bug (FR-012/FR-013, User Story 5)

**Decision**: Replace `useChatStream`'s "seed once, guarded by `initializedRef`" pattern with a
persistent "sync from the query until the user sends in this view" pattern, gated by a
`hasSentRef` flag that is set only inside `send`/`sendImage`/`sendTranslation`, never implicitly
from whether `initialMessages` happened to be defined on a given render.

**Rationale**: The existing guard conflates two different things under one flag
(`initializedRef.current = initialMessages !== undefined`, and the seeding `useEffect` only ever
applies `initialMessages` once, when `!initializedRef.current`):

1. "Has the user started actively sending in this specific mounted view?" (the guard this flag
   was actually added for, per its own comment — protecting a live, in-progress `send()` from
   being clobbered by a same-view, mid-stream `useChatMessages` fetch that necessarily captures
   an incomplete snapshot, since the assistant's reply is only persisted once the stream
   finishes).
2. "Has this view ever received a defined value for `initialMessages`?" — which becomes true the
   *first time TanStack Query returns anything at all*, including a stale-but-cached empty array.

When a brand-new chat is created mid-session (`ensureChatId` → `onChatCreated` → parent sets
`selectedChatId`, without remounting `ConversationView`, by design — see the "2026-07-28
ChatGPT-style history decision" comment in `ChatPage.tsx`), `useChatMessages(chatId)` starts
fetching that brand-new chat's messages *while the reply is still streaming*. That fetch
resolves with `{ items: [], nextCursor: null }` (nothing persisted yet) and TanStack Query caches
it under `['chats', <newChatId>, 'messages']`. Meaning (2) becomes true immediately in that same
view, correctly gated from clobbering the live conversation by (1) also being true (the user is
mid-send) — so no bug is visible yet.

The bug surfaces on the *next* mount: when the user later reopens that same conversation,
`ConversationView` mounts fresh, `useChatMessages(chatId)` reads the *same query key* — and
because the previous empty snapshot is still cached, TanStack Query returns it synchronously
(`isPending: false`, cached stale data) before its background refetch resolves. On this fresh
mount, `initialMessages` is therefore defined (as `[]`) on the very first render, so
`initializedRef.current` initializes to `true` immediately — before the background refetch (which
would return the real, persisted messages) ever completes. Because the seeding `useEffect` only
acts when `!initializedRef.current`, the corrected data that arrives moments later is silently
discarded, and `messages` stays `[]` forever for that mount: a blank pane, per User Story 5.

**Fix**: `hasSentRef` starts `false` on every fresh mount and is set `true` only inside `send`/
`sendImage`/`sendTranslation` — never derived from whether `initialMessages` is defined. The
seeding `useEffect` drops the "only once" restriction and instead re-applies `initialMessages` to
`messages` on *every* change, for as long as `!hasSentRef.current` — meaning a fresh mount keeps
tracking the query's data (including a corrected background refetch) right up until the user
actively sends something new in that view, at which point (matching the original, still-valid
intent) the locally-tracked streaming conversation takes over and stops syncing from the query
until the next mount.

**Side effect (beneficial, not separately scoped)**: the same "only once" restriction was also
silently preventing a long conversation's *later* paginated pages (fetched in the background via
the existing `fetchNextPage` effect, per FR-024) from ever reaching the displayed `messages` after
the first page resolved. The fix above resolves that too, as the identical root cause, without
requiring separate work.

**Alternatives considered**:
- *Skip/disable the `useChatMessages` fetch entirely while `isStreaming` is true for a
  same-view, mid-send chat-creation.* Would prevent the premature empty snapshot from being
  fetched at all, but doesn't address the deeper issue: the seeding effect's "only once" gate is
  wrong regardless of what triggered the first (possibly stale) `initialMessages` value, and would
  leave the analogous pagination bug (see above) unfixed.
- *Invalidate/refetch the specific chat's messages query once its stream completes.* Would paper
  over the stale-cache symptom for this one scenario but not fix the underlying flag conflation,
  and would need to be remembered for every future code path that seeds `messages` from a query.
