# Quickstart: Validating Chat Loading & Reply Feedback Fixes

## Prerequisites

- Backend running (`AskLucy.Web`, e.g. via `dotnet run` from `src/AskLucy.Web`) with at
  least one authenticated user account that has 2+ existing conversations with prior
  messages.
- Frontend dev server running from `src/AskLucy.Web/ClientApp`:
  ```
  npm run dev
  ```
- Browser DevTools open (for network throttling in Scenario 1 and reduced-motion emulation
  in Scenario 4).

## Scenario 1 — Conversation switch never shows the wrong empty state (User Story 1 & 2)

1. In DevTools → Network, set throttling to "Slow 3G" (or similar) so message fetches take
   >1s.
2. Open the chat history panel; click a past conversation that has messages.
3. **Expected**: A loading spinner appears in the chat area almost immediately (~100ms),
   *not* the "Start a conversation with Ask Lucy." placeholder.
4. Wait for the fetch to resolve. **Expected**: the spinner is replaced by that
   conversation's messages.
5. Repeat, but this time click a second conversation before the first one's spinner clears.
   **Expected**: the chat area ends up showing the second (last-clicked) conversation's
   messages once loading settles — never a blank/mixed state.
6. Disable network throttling; in DevTools → Network, block the conversation-messages
   request (right-click → "Block request URL") and click a conversation.
   **Expected**: a visible error state with a "Retry" button appears (not an indefinite
   spinner). Unblock the request and click "Retry" — the conversation's messages load
   normally.
7. Click "New chat". **Expected**: the "Start a conversation with Ask Lucy." empty state
   appears — this is the one case where it is correct.

## Scenario 2 — Thinking indicator while awaiting a reply (User Story 3)

1. Open any conversation (or start a new chat) and send a message.
2. **Expected**: an animated three-dot indicator appears in the reply-bubble area almost
   immediately (~100ms), before any response text is visible.
3. **Expected**: as soon as the model's first streamed token arrives, the dots are replaced
   by the incoming text — no lingering overlap of both.
4. To exercise the failure path: block the `ai/chat` request in DevTools → Network, then
   send a message. **Expected**: the dots are replaced by a visible error (Snackbar) with a
   "Retry" action; clicking it resends the same message.

## Scenario 3 — No provider/model attribution shown (User Story 4)

1. Open a conversation with at least one assistant reply (existing history is fine — no new
   message needs to be sent).
2. **Expected**: no "Provider · Model" style caption (e.g. "OpenAI · gpt-3.5-turbo") appears
   anywhere in the reply bubble, for both older and newly generated replies.
3. Confirm data is still recorded, not just hidden: check that a reply's underlying
   `provider`/`model` fields are still present in the network response payload for
   `GET /api/v1/chats/{id}/messages` (DevTools → Network → response body) — only the visual
   caption is gone.

## Scenario 4 — Indicators are not gated behind reduced-motion (edge case check)

1. In DevTools, enable "Emulate CSS media feature `prefers-reduced-motion: reduce`"
   (Rendering tab).
2. Repeat Scenario 1 step 2 and Scenario 2 step 1.
3. **Expected**: both the loading spinner and the three-dot thinking indicator continue to
   animate exactly as before — per the spec clarification, no static/reduced-motion variant
   is implemented for this feature.

## Automated checks

From `src/AskLucy.Web/ClientApp`:

```
npm run test    # Vitest + React Testing Library, includes updated MessageBubble tests,
                 # new ThinkingIndicator tests, and ConversationView loading/error branch tests
npm run lint
```

Accessibility: the new loading/error/thinking-indicator states should be covered by a
`jest-axe` check following the existing pattern in
`src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSidebar.a11y.test.tsx`.
