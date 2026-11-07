namespace AskLucy.Application.Spatial.Query;

/// <summary>specs/095 — comparison operators supported by attribute predicates.</summary>
public enum AttributeOperator
{
    Gt, Gte, Lt, Lte, Eq, NotEq, Between,
    Contains, StartsWith, EndsWith,
    InYearRange,
}

/// <summary>
/// specs/095 — one filter condition applied to a dataset attribute field.
/// </summary>
/// <param name="Field">Must match an <see cref="AttributeField.Name"/> on the target dataset.</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Value">The comparison value, pre-parsed to the field's type.</param>
/// <param name="ValueTo">Upper bound for <c>Between</c> and <c>InYearRange</c>.</param>
/// <param name="Unit">User-supplied unit; converted to canonical before comparison.</param>
public sealed record AttributePredicate(
    string Field,
    AttributeOperator Operator,
    object Value,
    object? ValueTo = null,
    string? Unit = null);
