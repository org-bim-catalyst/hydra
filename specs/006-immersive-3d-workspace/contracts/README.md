# Contracts: Immersive 3D AI Workspace

**No new API contracts.** This feature only changes how the `/chat` route is presented
in `AskLucy.Web/ClientApp`; it introduces no new backend endpoints, request/response
DTOs, or OpenAPI surface changes (see [data-model.md](../data-model.md)).

The redesigned UI continues to call the existing, already-documented endpoints through
the existing frontend API modules, unchanged:

- `features/chat/api/chatsApi.ts` — list/search conversations, rename, pin/favorite/
  archive/restore, duplicate, export, clear, purge, get messages.
- `features/chat/api/aiApi.ts` — send message, streamed AI response, translation, image
  generation.

If a future iteration of this feature migrates voice output to a server-rendered audio
stream (research.md §3's flagged follow-up), that would introduce a real new contract and
requires its own spec/plan — it is explicitly out of scope here.
