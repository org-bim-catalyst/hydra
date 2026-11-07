namespace AskLucy.Application.Spatial.Query;

/// <summary>specs/095 — how predicates within one group are combined.</summary>
public enum PredicateCombinator { And, Or }

/// <summary>
/// specs/095 — a typed group of same-kind predicates with their combinator.
/// OR may only join predicates within the same type group (clarification Q4).
/// </summary>
public sealed record PredicateGroup<T>(
    IReadOnlyList<T> Predicates,
    PredicateCombinator Combinator = PredicateCombinator.And);
