namespace AskLucy.Web.Contracts;

/// <summary>specs/080 contracts "PUT": all three values are required.</summary>
public sealed record UpdatePresenceSphereSettingsRequest(decimal DotSizeMultiplier, int CardFillPercent, bool ZoomEnabled);
