namespace AskLucy.Infrastructure.Buildings.Overture;

/// <summary>
/// specs/075 — Overture Maps building footprints, read straight from the Foundation's public
/// PMTiles archive with HTTP range requests. Bound from "Buildings:Overture"; every setting has a
/// working default, so the section can be absent.
/// </summary>
public sealed class OvertureBuildingsOptions
{
    public const string SectionName = "Buildings:Overture";

    /// <summary>Takes Overture out of the footprint chain without a deploy.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The public S3 bucket holding one "tiles/{release}/" folder per Overture release.</summary>
    public string BucketUrl { get; set; } = "https://overturemaps-extras-us-west-2.s3.us-west-2.amazonaws.com/";

    /// <summary>
    /// Pins a release such as "2026-09-23.1". Empty means the newest one in the bucket — the
    /// Foundation deletes old releases, so a release hard-coded here eventually stops working.
    /// </summary>
    public string? Release { get; set; }

    /// <summary>How long the discovered release, its header and its directories are trusted.</summary>
    public TimeSpan ArchiveCacheTtl { get; set; } = TimeSpan.FromHours(24);
}
