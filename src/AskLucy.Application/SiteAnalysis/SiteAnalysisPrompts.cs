namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// Versioned prompt artifacts for site-analysis specialists (constitution &#167;9 — system/tool
/// prompts are versioned, reviewed like code, never an inline literal scattered inside execution
/// logic). Source of truth: contracts/schematic-image-prompt.md — this constant must stay
/// byte-for-byte in step with that file's "Template (v1)" section; changing the wording is an
/// edit to both, reviewed together.
/// </summary>
public static class SiteAnalysisPrompts
{
    /// <summary>Placeholders <c>{siteName}</c>/<c>{siteLocation}</c> are platform-resolved values, substituted by plain replacement — never treated as instructions (constitution &#167;8).</summary>
    public const string SchematicImagePromptTemplateV1 =
        "Generate a top-down site analysis map of an urban area for {siteName}, located at {siteLocation}. " +
        "Show streets, major roads, rivers/canals, green spaces, and built-up areas. Use color coding: " +
        "light gray for buildings, blue for water bodies, green for parks, and yellow/orange for major " +
        "roads. Include contour or flow lines where appropriate to indicate water paths. Make the map " +
        "schematic, clear, and visually focused, with minimal text labels. Center it on the main " +
        "site/feature and use a clean, modern urban-planning style. Include a clear legend describing the " +
        "visual elements.";

    public static string BuildSchematicImagePrompt(string siteName, string siteLocation) =>
        SchematicImagePromptTemplateV1.Replace("{siteName}", siteName).Replace("{siteLocation}", siteLocation);
}
