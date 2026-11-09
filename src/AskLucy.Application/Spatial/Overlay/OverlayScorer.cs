using System.Globalization;
using System.Text.Json;
using AskLucy.Application.Spatial.Query;

namespace AskLucy.Application.Spatial;

/// <summary>Deterministic 0–1 min/max scoring applied only to candidates that passed hard filters.</summary>
public static class OverlayScorer
{
    public static IReadOnlyList<QueryResultFeature> Score(
        IReadOnlyList<QueryResultFeature> candidates,
        IReadOnlyList<OverlayCriterion> criteria,
        OverlayScoringProfile? profile)
    {
        if (profile is null || candidates.Count == 0)
        {
            return candidates;
        }

        var byCriterion = criteria.ToDictionary(criterion => criterion.CriterionId, StringComparer.OrdinalIgnoreCase);
        var weights = profile.Weights.ToDictionary(weight => weight.CriterionId, weight => weight.Weight,
            StringComparer.OrdinalIgnoreCase);
        var normalizedValues = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var criterionId in weights.Keys)
        {
            if (!byCriterion.TryGetValue(criterionId, out var criterion)) continue;
            var values = candidates.Select(candidate => ReadNumber(candidate.Attributes,
                criterion.CriterionId) ?? ReadNumber(candidate.Attributes, criterion.Field)).ToArray();
            var available = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            var minimum = available.Length == 0 ? 0 : available.Min();
            var maximum = available.Length == 0 ? 0 : available.Max();

            normalizedValues[criterionId] = values.Select(value =>
            {
                if (!value.HasValue) return 0d;
                var normalized = maximum <= minimum ? 1d : (value.Value - minimum) / (maximum - minimum);
                return criterion.Direction == OverlayCriterionDirection.LowerIsBetter ? 1d - normalized : normalized;
            }).ToArray();
        }

        var totalWeight = weights.Values.Sum();
        if (!double.IsFinite(totalWeight) || totalWeight <= 0)
        {
            return candidates;
        }

        var scored = candidates.Select((feature, index) =>
        {
            var criterionScores = normalizedValues.ToDictionary(pair => pair.Key,
                pair => pair.Value[index], StringComparer.OrdinalIgnoreCase);
            var score = criterionScores.Sum(pair => pair.Value * weights[pair.Key]) / totalWeight;
            var eligibility = criteria.Where(criterion => criterion.Required)
                .ToDictionary(criterion => criterion.CriterionId, _ => true, StringComparer.OrdinalIgnoreCase);
            var previous = feature.Overlay;
            return (Feature: feature with
            {
                Overlay = new OverlayFeatureMetadata(eligibility, criterionScores, score, null,
                    previous?.Provenance ?? [feature.FeatureId]),
            }, Score: score);
        }).OrderByDescending(item => item.Score)
          .ThenBy(item => item.Feature.FeatureId, StringComparer.Ordinal)
          .ToList();

        return scored.Select((item, index) => item.Feature with
        {
            Overlay = item.Feature.Overlay! with { Rank = index + 1 },
        }).ToList();
    }

    private static double? ReadNumber(IReadOnlyDictionary<string, object?> attributes, string? key)
    {
        if (key is null || !attributes.TryGetValue(key, out var value) || value is null) return null;
        if (value is double d && double.IsFinite(d)) return d;
        if (value is float f && float.IsFinite(f)) return f;
        if (value is IConvertible convertible)
        {
            try
            {
                var parsed = convertible.ToDouble(CultureInfo.InvariantCulture);
                return double.IsFinite(parsed) ? parsed : null;
            }
            catch (FormatException) { return null; }
            catch (InvalidCastException) { return null; }
        }
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonNumber))
        {
            return double.IsFinite(jsonNumber) ? jsonNumber : null;
        }

        return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;
    }
}
