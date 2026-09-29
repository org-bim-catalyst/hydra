# Quickstart: Validating Hand-Edited Outlines

This guide checks the finished feature end to end. The contracts are in [contracts/](contracts/)
and the state in [data-model.md](data-model.md).

## Prerequisites

- The migration `AddSiteBoundaryCorrections` is applied: automatically at startup for dev and
  prod, and **by hand for the test2 database** used by Persistence.Tests.
- Backend tests: `PERSISTENCE_TESTS_CONNECTION_STRING` is set (Web.Tests) and
  `PERSISTENCE_TESTS_2_CONNECTION_STRING` is set (Persistence.Tests).
- The Boundary vision setting "Include connected buildings" is on (the default).

## Automated checks

```bash
dotnet build "Ask Lucy.sln"
dotnet test tests/AskLucy.Domain.Tests
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~SiteBoundary"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~NtsSiteRingGeometry"
dotnet test tests/AskLucy.Persistence.Tests
dotnet test tests/AskLucy.Web.Tests --filter "FullyQualifiedName~SiteBoundaryEdit"

cd src/AskLucy.Web/ClientApp
npx tsc -b --noEmit
npx vitest run            # full suite: ChatPage tests assert offers independently
```

Expected: all pass. The endpoint tests cover 200, 400, 404 for a non-owner, 409 for a stale
revision, and 422 for self-crossing and drift.

## Manual run (production or localhost:7170)

Each step says what to screenshot and the expected value.

1. **Offer after a building choice.** In a new chat: "Show me Muscat Grand Mall", then pick B
   ("with its nearby buildings").
   - Expect the outline with a Phase 2 ring (about 48,860 m²).
   - Then an offer: "I'm fairly sure about this outline — want to adjust its corners?"
   - Its rows: **Edit the outline** first, analysis rows under it, **It looks right** last.
2. **Entry.** Before accepting, set the map to 3D and rotating. Screenshot it. Then pick **Edit
   the outline**.
   - Expect rotation to stop, a top-down, north-up view framing both rings, and corner handles
     on the ring you last touched.
   - The toolbar shows "Editing: Muscat Grand Mall" and an area.
   - The 3D/plan and rotation buttons are greyed out.
3. **Move, add, delete, undo.**
   - Drag one Phase 2 corner onto the building's edge.
   - Drag an edge's midpoint out.
   - Right-click another corner and choose Delete.
   - Press Undo once: only the delete is reverted.
   - The area changes after each step.
4. **Refusals.**
   - Drag a corner across the opposite edge. It snaps back with "That would make the outline
     cross itself."
   - On a 3-corner ring, try Delete. It is refused with "An outline needs at least 3 corners."
5. **Done and restore.** Press **Done**.
   - The outline redraws with the animated border and a hand-edited mark.
   - The chat gains "You edited the outline of Muscat Grand Mall — now N m²."
   - The map is back in 3D, rotating, at the step-2 zoom. Compare with the step-2 screenshot:
     heading within 1°.
6. **Persistence.** Reload the page and reopen the chat. The same corners are there, still marked
   hand-edited, and the chat line is still present.
7. **Lucy knows.** Ask "How big is the site?". The answer is the edited area from step 5, not
   48,860.
8. **Another chat.** Start a new chat: "Show me Muscat Grand Mall".
   - Expect the edited outline, with no building question and no edit offer.
   - Lucy says it's your corrected outline. The offer shows **Reset to Lucy's outline** first.
9. **Building choice keeps edits.** In the first chat, choose "Muscat Grand Mall only".
   - Phase 2's ring disappears. The mall ring's hand-placed corners are unchanged.
   - The chat says the edits were kept.
10. **Cancel.** Open the editor, move a corner, press **Cancel**. The outline is exactly as
    before, nothing is saved, and the view is restored.
11. **Two tabs.** Open the chat in two tabs. Save an edit in tab A, then press Done in tab B.
    - Expect "The outline changed in another tab." with **Load latest**.
    - Load latest shows tab A's shape.
12. **Reset.** Press **Reset to Lucy's outline** on the map and confirm.
    - The found outline redraws animated, without the hand-edited mark.
    - A chat line reads "…back to the one I found — 34,065 m²".
    - The chat from step 8, reopened, now shows Lucy's outline too.
13. **Keyboard only.**
    - Tab into the editor, step through the corners (they are announced), and move one with the
      arrow keys (0.5 m per press).
    - Insert adds a corner, Delete removes one, and Ctrl+Z undoes.
    - Enter on Done saves.
14. **Other sites** (SC-004). Repeat steps 2–5 briefly for BurJuman (Dubai) and the specs/077
    Muscat set: Oman Avenues Mall, Royal Opera House Muscat, Mall of Oman, Sultan Qaboos Grand
    Mosque, Mutrah Souq, Al Alam Palace.
15. **Large ring** (SC-005). On a traced park with hundreds of corners, drag a corner on the RTX
    4060 machine. The outline keeps up with the pointer.
