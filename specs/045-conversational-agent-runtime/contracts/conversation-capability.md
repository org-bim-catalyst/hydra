# Contract: Conversation Capability

**Feature**: 045-conversational-agent-runtime | **Layer**: `AskLucy.Application/Conversations/Capabilities`

The extension seam that lets an agent tool describe itself to a model *and* to a user, and declare when it is usable. Modelled on Anthropic's Agent Skills architecture (research.md D13). Satisfies FR-010 – FR-014.

---

## Three tiers of disclosure

| Tier | Audience | When loaded | Members |
|---|---|---|---|
| **1 — Index** | the deciding model | every turn, for every available capability | `Name`, `Description`, `WhenToUse`, `ArgumentHint` |
| **2 — Guidance** | the sub-agent running it; the narration step | only for a capability actually selected | `UsageGuidance` |
| **3 — Contract** | server-side validation and authorization | **never shown to any model** | `InputSchemaJson`, `OutputSchemaJson`, `RequiredPermissions`, `RiskLevel` |
| **User-facing** | the person reading an offer | when offered | `Label`, `OfferDescription`, `AcknowledgementTemplate` |

Tier 3 never reaching a model is what makes the grounding guarantee hold: the model cannot shape its arguments around a schema it has not seen, so `SuggestedActionGrounder`'s validation is an independent check rather than a formality (research.md D10).

---

## Interface

```csharp
namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// An <see cref="IAgentTool"/> that Lucy may invoke during a conversation turn and may offer
/// to the user as a next action. Opt-in: a tool becomes conversational by implementing this
/// interface, and the orchestrator never changes (FR-012).
/// </summary>
public interface IConversationCapability : IAgentTool
{
    // ---- Tier 1: index. Shown to the deciding model every turn. ----

    /// <summary>
    /// When this capability is the right choice, in third person, with concrete trigger terms.
    /// Pairs with the inherited <see cref="IAgentTool.Description"/> ("what it does") to form
    /// the complete index entry — Anthropic's skill guidance requires a description to carry
    /// BOTH halves, and "what it does" alone gives the model nothing to route on.
    /// Max 300 chars. Third person only: never "I can…" or "You can use this to…".
    /// </summary>
    /// <example>
    /// "Use when the user asks to see, find, locate or navigate to a named real-world place,
    /// or mentions a site, park, building or address they want shown on the map."
    /// </example>
    string WhenToUse { get; }

    /// <summary>Plain-language hint at what this needs, e.g. "the place name". Not a schema. Max 80 chars.</summary>
    string ArgumentHint { get; }

    // ---- Tier 2: guidance. Loaded only when this capability is selected. ----

    /// <summary>
    /// How to use this capability well, what its outputs mean, how to report its result to the
    /// user, and what its failure modes look like. The skill body. Keep it short — every token
    /// competes with conversation history once loaded; assume the model is already capable and
    /// document only what it cannot know.
    /// <para>
    /// Match specificity to fragility. A fragile, expensive capability gets a prescribed
    /// sequence; an open-ended one gets heuristics.
    /// </para>
    /// </summary>
    string UsageGuidance { get; }

    // ---- User-facing. ----

    /// <summary>Short user-facing name for the offer card. Spoken aloud (FR-044). Max 80 chars.</summary>
    string Label { get; }

    /// <summary>One line under the label. Never spoken (FR-044). Max 160 chars.</summary>
    string OfferDescription { get; }

    /// <summary>
    /// The acknowledgement beat's wording when this capability is selected — "OK, let me find
    /// it first." Templated, never model-generated (research.md D15), so the first thing the
    /// user sees is never behind a network round trip and never silenced by a failed decision
    /// step. Localised through the standard resource pipeline.
    /// </summary>
    string AcknowledgementTemplate { get; }

    // ---- Availability and cost. ----

    /// <summary>
    /// Whether this <b>could run</b> against the given turn state. Gates what the deciding model
    /// may see and invoke. Pure and synchronous — every input is already on
    /// <see cref="TurnContext"/>; an implementation needing I/O to decide is declaring the wrong
    /// precondition.
    /// </summary>
    bool IsAvailable(TurnContext context);

    /// <summary>
    /// Whether running this would <b>plausibly be wanted next</b>, given what just happened.
    /// Gates what may be <i>offered</i> (FR-025b) — a stricter test than
    /// <see cref="IsAvailable"/>, and deliberately a separate question.
    /// <para>
    /// A capability that is always invocable is not thereby always worth suggesting.
    /// <c>resolve_location</c> can run at any moment, but offering "find a place" after every
    /// answer about setback regulations is noise. Defaults to <see cref="IsAvailable"/> where
    /// the two genuinely coincide.
    /// </para>
    /// </summary>
    bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => IsAvailable(context);

    /// <summary>
    /// Roughly how long a successful invocation takes. Drives FR-005a: anything above
    /// <see cref="CapabilityDuration.Brief"/> is announced as its own message before it runs.
    /// </summary>
    CapabilityDuration ExpectedDuration { get; }
}

public enum CapabilityDuration
{
    /// <summary>Sub-second. Runs inline; no announcement beat.</summary>
    Brief,

    /// <summary>A few seconds. Announced with a pending label.</summary>
    Noticeable,

    /// <summary>Tens of seconds. Announced, and never started unless asked for or accepted (FR-046).</summary>
    Long,
}
```

`Name`, `Description`, `RiskLevel`, `RequiredPermissions`, `InputSchemaJson`, `OutputSchemaJson` and `ExecuteAsync` are inherited from `IAgentTool` **unchanged**, so every existing runtime guarantee — permission checks, policy evaluation, approval gating, duplicate detection, audit logging — applies without modification (FR-015).

---

## Writing an index entry

The index entry is `Description` + `WhenToUse`. It is the *only* thing the model sees when choosing, so it carries the whole routing burden.

**Rules** (from Anthropic's skill-authoring guidance):

1. **Both halves.** What it does, and when to use it. Either alone is insufficient.
2. **Third person.** "Resolves a named place to coordinates", never "I can find places for you".
3. **Concrete trigger terms.** Name the words a user actually says — "see", "find", "show me", "where is", "take me to". These are what the model matches against, and what the embedding retriever (research.md D14) indexes.
4. **Specific, not generic.** "Handles locations" is useless; "Resolves a named real-world place to confirmed coordinates and recentres the map viewer on it" is not.
5. **Consistent vocabulary** across all capabilities — one word per concept. Do not mix "place", "site", "location" and "POI" across entries.
6. **No time-sensitive statements.** Nothing that becomes false at a date.

**Good**

```text
resolve_site_boundary
  Description: Finds the outline of a site around a confirmed location and draws it on the map,
               reporting its area and how confident the match is.
  WhenToUse:   Use when a location is already confirmed and the user asks to outline, highlight,
               show the extent of, or measure the site — or accepts an offer to do so.
  ArgumentHint: nothing; it uses the location already confirmed this turn
```

**Bad**

```text
resolve_site_boundary
  Description: Handles site boundaries.          ← what does "handles" mean? no triggers
  WhenToUse:   I can outline sites for you.      ← first person; no trigger terms
```

---

## Catalog

```csharp
public sealed class ConversationCapabilityCatalog(
    AgentToolCatalog toolCatalog,
    IEmbeddingService embeddingService,
    IOptions<ConversationRuntimeOptions> options)
{
    /// <summary>
    /// The Tier 1 index for this turn — the only capabilities the deciding model may see or
    /// invoke (FR-014). Above <c>IndexRetrievalThreshold</c> entries, narrowed by embedding
    /// similarity to the user's message, always retaining every context-gated available
    /// capability (research.md D14).
    /// </summary>
    public Task<IReadOnlyList<CapabilityIndexEntry>> BuildIndexAsync(
        TurnContext context, string userMessage, CancellationToken cancellationToken);

    /// <summary>Every available capability, unnarrowed. Used by the offer step (FR-021).</summary>
    public IReadOnlyList<IConversationCapability> AvailableFor(TurnContext context);

    /// <summary>Resolves a key. Used at dispatch time to re-check availability (FR-028).</summary>
    public IConversationCapability? Find(string capabilityKey);
}

public sealed record CapabilityIndexEntry(string Key, string Description, string WhenToUse, string ArgumentHint);
```

`AgentToolCatalog.All` is read live on every call, so an MCP server going active or inactive is reflected in the very next turn without a restart.

**Retrieval never decides.** It narrows what the model is shown; the model still selects. A miss costs a missed capability, never a wrong action.

---

## Registered capabilities (FR-013)

| Key | Label | **Available** when (may run) | **Offerable** when (worth suggesting) | Duration | Permissions |
|---|---|---|---|---|---|
| `resolve_location` | Find a place | always | **never as a step** — offered only as part of a `locate_a_place` variant (FR-060) | Noticeable | `ExternalNetwork` |
| `resolve_site_boundary` | Highlight the site boundary | `ActiveLocation is not null` **and** `ActiveBoundary?.SiteName != ActiveLocation.LocationName` | **never as a step** — reached through the `full` variant | **Long** | `ExternalNetwork` |
| `adjust_viewer_focus` | Zoom the viewer | `ActiveLocation is not null` | **never as a step** — reached through either variant | Brief | — |
| `search_knowledge_base` | Search my knowledge bases | `AttachedKnowledgeBaseIds.Count > 0` | same, **and** the turn produced a subject worth searching for, **and** it was not already searched this turn | Noticeable | `ReadKnowledge` |
| `search_memory` | Check what I remember | `IsMemoryAvailable` | **never** — an internal lookup, not a user-facing choice | Brief | `ReadMemory` |
| `open_visual_panel` | Show it as a panel | room in `OpenPanelTypeKeys` **and** a registered panel type fits the data | same, **and** the turn produced data a panel would actually render | Brief | — |
| `mcp:{server}:{tool}` | server-declared title | the MCP server is active **and** the user holds every `RequiredPermission` | defaults to available; a server may mark a tool non-offerable | Noticeable | per tool |

Note how few capabilities are offerable: **five of the seven are never offered at all.** Three of those five are steps of the `locate_a_place` flow (see [capability-flow.md](./capability-flow.md)) and are reached through it, never advertised individually — offering a step of a job the user already started is asking them to authorise their own request twice. The other two are internal lookups. An offer that lists everything possible is a menu, not a suggestion.

---

## Availability versus offerability

Two different questions the original design conflated (research.md D16).

**`IsAvailable` — could this run?** Gates the Tier 1 index and invocation (FR-014). Three rules:

1. **Precondition** — required turn state is present (a boundary needs a location).
2. **Non-redundancy** — the result is not already in hand.
3. **Entitlement** — the user holds every `RequiredPermission` and their tier includes it.

Rule 3 is enforced by the catalog so a new capability cannot forget it; rules 1 and 2 belong to the capability.

**`IsOfferable` — would this plausibly be wanted next?** Gates the offer only (FR-025b). Available is necessary but not sufficient; add:

4. **Volunteerability** — this is something a user would want *suggested*, rather than something they simply ask for when they want it.
5. **Consequence** — the turn that just completed created a reason to consider it. A boundary is worth offering because a site was just found; it is not worth offering three turns later.
6. **Novelty** — it was not just run, not just offered and ignored, and not just declined.

Rules 4–6 are why an unconditioned capability like `resolve_location` is available on every turn yet offered on none.

An unavailable capability is invisible to the turn: not offered, not in the index, not invocable (FR-014). A capability that is available but not offerable is fully usable — the user need only ask. An available capability whose policy requires approval is offered as an action the user confirms, never invoked silently.

**When nothing is offerable, the offer step does not run at all** — no model call, no event, no card (FR-025a). Ending a turn in silence is a normal outcome, not a degraded one.

---

## Testing contract

Every implementation ships:

1. **Availability tests** — `false` for each missing precondition independently; `false` when the result is already present.
2. **Index-entry tests** — `Description` and `WhenToUse` non-empty, within length limits, third person (no `"I "`, `"I'm"`, `"you can"`), and `WhenToUse` contains at least two concrete trigger terms.
3. **Execution test** — valid input produces output validating against `OutputSchemaJson`.
4. **Failure test** — a failing dependency returns `AgentToolResult.Failure` with a reason; never throws into the turn (constitution §2.VIII).
5. **At least three evaluation scenarios**, written *before* the `UsageGuidance` prose: a representative request, the expected routing decision, and the expected user-visible outcome. Per the skill guidance, evaluations come first so guidance answers observed failures rather than imagined ones.

Points 2 and 4 are asserted by a **shared theory test over every registered capability**, so a new capability cannot be added without satisfying them.
