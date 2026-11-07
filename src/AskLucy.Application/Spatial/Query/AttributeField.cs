namespace AskLucy.Application.Spatial.Query;

/// <summary>specs/095 — describes one queryable attribute field on a geographic dataset.</summary>
public enum AttributeFieldType { Numeric, String, Year, Date }

/// <summary>
/// specs/095 — one attribute field exposed by a <see cref="DatasetEntry"/>.
/// </summary>
/// <param name="Name">Machine key used in predicates (e.g. <c>area</c>, <c>height</c>).</param>
/// <param name="DisplayName">User-facing label (e.g. "Area", "Building Height").</param>
/// <param name="FieldType">The value type; determines which operators are valid.</param>
/// <param name="Unit">Canonical unit (e.g. <c>m2</c>, <c>m</c>). Null when unitless.</param>
/// <param name="ExampleValue">Example value shown in clarifications.</param>
public sealed record AttributeField(
    string Name,
    string DisplayName,
    AttributeFieldType FieldType,
    string? Unit = null,
    string? ExampleValue = null);
