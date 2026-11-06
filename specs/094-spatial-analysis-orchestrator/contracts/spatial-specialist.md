# Contract: Specialist Contract

**Spec**: SPEC-094 | **Requirements**: FR-006, FR-007, FR-008, FR-009, FR-010, FR-025, FR-028

The only route by which a spatial capability contributes work. A specialist performs retrieval/analysis; it does
not talk to the user and does not touch the viewer.

## Interface

`AskLucy.Application.Spatial.ISpatialSpecialist`

```csharp
public interface ISpatialSpecialist
{
    string OperationKind { get; }

    Task<SpatialSpecialistOutcome> ExecuteAsync(
        SpatialOperation operation,
        SpatialExecutionContext context,
        CancellationToken cancellationToken);
}
```

### `SpatialExecutionContext`

Carries only what a specialist may legitimately need:

```csharp
public sealed record SpatialExecutionContext(
    string? UserId,
    Guid UserChatId,
    ResolvedStudyArea? StudyArea,
    MapViewportContext? MapContext,
    IReadOnlyList<ActiveSpatialLayer> ActiveLayers);
```

It deliberately does **not** carry `TurnContext`: specialists must not depend on the conversation runtime, and
must not be able to deliver a message or a layer.

### `SpatialSpecialistOutcome`

```csharp
public sealed record SpatialSpecialistOutcome(
    SpatialOperationStatus Status,        // Succeeded | Empty | Failed
    IReadOnlyList<SpatialLayerDraft> Layers,
    IReadOnlyList<DataSourceCoverage> Coverage,
    string? FailureReason,                // diagnostic only, never shown verbatim
    IReadOnlyList<string> Warnings);
```

- `Empty` means the retrieval succeeded with zero matches. It is not a failure.
- `Failed` must include a `FailureReason`. Throwing is also handled: the coordinator catches, records `Failed`,
  and continues other operations.
- `Layers` are **drafts**: the coordinator validates and normalizes them before they become `SpatialLayer`s.

## Rules

| Rule | Requirement |
|---|---|
| Registration is the only integration step | A specialist is a DI-registered `ISpatialSpecialist`; adding one must not edit the coordinator, the registry, or the viewer (FR-006/FR-028). |
| One operation kind per specialist | `OperationKind` is a stable lower-case string (e.g. `poi.discovery`). Two specialists for the same kind is a startup error. |
| No user-facing delivery | A specialist must never write a message, call a notifier, push a panel, or emit a layer command (FR-009). |
| Authorization is the specialist's job | Provider calls must run as the requesting user and within the resolved study area (FR-010). |
| No silent failure | Every catch path returns `Failed` with a reason or rethrows for the coordinator to record; nothing is swallowed (FR-027). |
| Cancellation is honoured | The token must be passed to every provider call; `OperationCanceledException` propagates. |
| Output is untrusted | Layers are validated for kind, geometry, ranges, attribute allowlist and name length before they reach the viewer. |
| Provider failure ≠ empty | A provider being unavailable must never be reported as `Empty` (spec 093 contract; spec 090 precedent). |

## Lifecycle within one request

```text
coordinator interprets bundle
  → validates bundle
  → for each operation (respecting dependsOn):
        registry.TryGet(operationKind) → specialist
        specialist.ExecuteAsync(...) → outcome
        coordinator records outcome (never throws past this point)
  → normalizes all outcomes into SpatialAnalysisResult
  → validates + builds SpatialLayerCommand
  → emits one stream event (and one narration)
```

## Adding a specialist (the FR-028 proof)

1. Add a class implementing `ISpatialSpecialist` with a new `OperationKind`.
2. Register it in `src/AskLucy.Application/DependencyInjection.cs` as `ISpatialSpecialist`.
3. Add its Tier-1 interpretation wording to the coordinator's source of truth for supported kinds (a
   data declaration, not a switch statement) so the bundle builder can produce that kind.
4. If the result maps onto an existing layer kind (`markers`, `buffer`, `area`, `band`), no viewer change is
   needed. A genuinely new geometry representation is a separate viewer feature and is outside FR-028's
   guarantee (spec Assumptions).

No edit to `SpatialAnalysisCoordinator`, `SpatialResultNormalizer`, `SpatialAnalysisCapability`,
`SpatialLayerCommand`, the SSE writer, or the client store is required.
