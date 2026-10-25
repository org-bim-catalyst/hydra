# Contract: Environmental Provider Seam

**Spec**: [../spec.md](../spec.md) | **Plan**: [../plan.md](../plan.md) | **Requirements**: FR-016–FR-020, FR-030,
FR-035, FR-038

The two seams SPEC-057 declared (`IFloodDataProvider`, `IClimateDataProvider`) and the empty implementation this
feature supplies. This contract exists so that **replacing** the empty implementation with a licensed source is a
registration change, not a specialist change.

## Interfaces (unchanged from SPEC-057)

```csharp
Task<DataResolutionOutcome<FloodData>> ResolveAsync(double latitude, double longitude, CancellationToken ct = default);
Task<DataResolutionOutcome<ClimateData>> ResolveAsync(double latitude, double longitude, CancellationToken ct = default);
```

Records (unchanged):

- `FloodData(string? FloodZoneClassification, double? AnnualExceedanceProbabilityPercent)`
- `ClimateData(double? PrevailingWindDirectionDegrees, double? AverageWindSpeedMetersPerSecond)`

Outcome (unchanged): `DataResolutionOutcome<T>(DataResolutionOutcomeType Type, T? Value, string? Reason)` with
`Confirmed`, `NoCandidates`, `Ambiguous`, `Unavailable`. Only `Confirmed` and `Unavailable` have factories;
the other two are constructed through the record.

## The implementation shipped by this feature

| Class | Location | Behaviour |
|---|---|---|
| `UnavailableFloodDataProvider` | `src/AskLucy.Infrastructure/SiteAnalysis/Providers/` | always `DataResolutionOutcome.Unavailable<FloodData>("No flood data source is configured.")` |
| `UnavailableClimateDataProvider` | same | always `DataResolutionOutcome.Unavailable<ClimateData>("No climate data source is configured.")` |

Registered in `src/AskLucy.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<IFloodDataProvider, UnavailableFloodDataProvider>();
services.AddScoped<IClimateDataProvider, UnavailableClimateDataProvider>();
```

Rules:

- Must not call any external service, read any credential, or introduce any configuration.
- Must not throw. Unavailability is an outcome, not an error (FR-035).
- Must be registered even though empty: an unresolved dependency in an `IAgentTool` breaks
  `AgentToolCatalog`'s `IEnumerable<IAgentTool>` resolution for every request.
- Must be the **only** registration of each interface. When a licensed source is added, it replaces these
  registrations; it does not sit beside them.

## Requirements on a licensed implementation (future)

1. **Credential order**: resolve the credential from stored configuration first; read application configuration
   only when no stored credential exists (`docs/ARCHITECTURE.md`, "Credential resolution order"). A stored
   credential that cannot be decrypted is a *credential unreadable* state — never a silent fallback.
2. **Outcome honesty**: return `NoCandidates` when the source is reachable and has no entry for the location;
   `Ambiguous` when it returns conflicting values; `Unavailable` only when no usable source is reachable.
   Never coerce a missing value into a default.
3. **Range**: a resolved value outside the figure's meaningful range must be treated as unavailable by the
   specialist; providers should not emit one, but the specialist guards regardless (FR-015).
4. **Source identity**: `DataResolutionOutcome` carries no source name. A licensed implementation must therefore
   supply one — an additional member on the outcome, or an equivalent — for the finding to satisfy FR-030
   truthfully when a value is confirmed. Until then, `DataSource` is `"no data source configured"` and no figure
   is confirmed.
5. **Bounded work**: one call per figure group per run; the specialist calls each seam once and does not retry.
   Any retry policy belongs to the implementation and must be bounded (FR-040).
6. **Failure vocabulary**: an implementation may throw only for genuine faults; a fault is captured by the
   specialist and reported as a specialist failure (FR-034, FR-038). It must not be used to signal "no data".

## What the specialist must never do with a seam

- Call it with a name or a string location — it takes coordinates only.
- Treat an outcome as a failure, or an exception as an outcome.
- Render a confirmed value without marking whether it is `Confirmed`, `NotReported`, or out of range.
- Add a second seam, a second provider, or a fallback source of its own.
