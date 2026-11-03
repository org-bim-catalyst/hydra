namespace AskLucy.Application.PointsOfInterest;

/// <summary>
/// Maps what a user calls a category ("schools", "mosques", "petrol stations", "مدارس") onto the
/// OpenStreetMap tags that express it (specs/090). A closed catalog rather than a free-form tag
/// search: an arbitrary string cannot be turned into a trustworthy Overpass query, and a guessed
/// one returns either nothing or the wrong thing. An unsupported category is answered honestly
/// with the list of what is supported.
/// </summary>
public sealed class PoiCategoryCatalog
{
    private static readonly PoiCategoryDefinition[] Definitions =
    [
        new("school", "Schools", "#4285F4", "classic-pin",
            [Group(("amenity", "school"))]),
        new("kindergarten", "Kindergartens", "#26A69A", "simple-dot",
            [Group(("amenity", "kindergarten"))]),
        new("university", "Universities", "#3949AB", "3d-highlight",
            [Group(("amenity", "university"), ("amenity", "college"))]),
        new("hospital", "Hospitals", "#EA4335", "classic-pin",
            [Group(("amenity", "hospital"))]),
        new("clinic", "Clinics", "#F06292", "simple-dot",
            [Group(("amenity", "clinic"), ("amenity", "doctors"))]),
        new("pharmacy", "Pharmacies", "#34A853", "simple-dot",
            [Group(("amenity", "pharmacy"))]),
        new("mosque", "Mosques", "#00897B", "pulsing-ring",
            [Group(("amenity", "place_of_worship"), ("religion", "muslim"))]),
        new("church", "Churches", "#7E57C2", "classic-pin",
            [Group(("amenity", "place_of_worship"), ("religion", "christian"))]),
        new("supermarket", "Supermarkets", "#FB8C00", "simple-dot",
            [Group(("shop", "supermarket"), ("shop", "convenience"))]),
        new("mall", "Shopping malls", "#8D6E63", "3d-highlight",
            [Group(("shop", "mall"))]),
        new("bank", "Banks", "#1E88E5", "simple-dot",
            [Group(("amenity", "bank"))]),
        new("atm", "ATMs", "#5E35B1", "simple-dot",
            [Group(("amenity", "atm"))]),
        new("restaurant", "Restaurants", "#F4511E", "classic-pin",
            [Group(("amenity", "restaurant"))]),
        new("cafe", "Cafés", "#6D4C41", "simple-dot",
            [Group(("amenity", "cafe"))]),
        new("park", "Parks", "#43A047", "3d-highlight",
            [Group(("leisure", "park"), ("leisure", "garden"))]),
        new("hotel", "Hotels", "#C2185B", "classic-pin",
            [Group(("tourism", "hotel"))]),
        new("fuel", "Petrol stations", "#546E7A", "simple-dot",
            [Group(("amenity", "fuel"))]),
        new("police", "Police stations", "#1565C0", "classic-pin",
            [Group(("amenity", "police"))]),
        new("fire_station", "Fire stations", "#D32F2F", "classic-pin",
            [Group(("amenity", "fire_station"))]),
        new("post_office", "Post offices", "#F9A825", "simple-dot",
            [Group(("amenity", "post_office"))]),
        new("bus_station", "Bus stations", "#00838F", "simple-dot",
            [Group(("amenity", "bus_station"))]),
        new("airport", "Airports", "#455A64", "3d-highlight",
            [Group(("aeroway", "aerodrome"))]),
        new("museum", "Museums", "#8E24AA", "classic-pin",
            [Group(("tourism", "museum"))]),
        new("library", "Libraries", "#6A1B9A", "simple-dot",
            [Group(("amenity", "library"))]),
        new("stadium", "Stadiums", "#EF6C00", "3d-highlight",
            [Group(("leisure", "stadium"))]),
        new("gym", "Gyms", "#00ACC1", "simple-dot",
            [Group(("leisure", "fitness_centre"))]),
        new("playground", "Playgrounds", "#9CCC65", "simple-dot",
            [Group(("leisure", "playground"))]),
        new("parking", "Parking", "#78909C", "simple-dot",
            [Group(("amenity", "parking"))]),
    ];

    /// <summary>Aliases a user may type, mapped to a catalog key. Plural/suffix stripping is applied as a fallback.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["schools"] = "school",
        ["مدارس"] = "school",
        ["مدرسة"] = "school",
        ["kindergartens"] = "kindergarten",
        ["nursery"] = "kindergarten",
        ["universities"] = "university",
        ["college"] = "university",
        ["colleges"] = "university",
        ["hospitals"] = "hospital",
        ["مستشفيات"] = "hospital",
        ["مستشفى"] = "hospital",
        ["clinics"] = "clinic",
        ["doctor"] = "clinic",
        ["doctors"] = "clinic",
        ["health centre"] = "clinic",
        ["health center"] = "clinic",
        ["pharmacies"] = "pharmacy",
        ["chemist"] = "pharmacy",
        ["صيدليات"] = "pharmacy",
        ["صيدلية"] = "pharmacy",
        ["mosques"] = "mosque",
        ["masjid"] = "mosque",
        ["masjids"] = "mosque",
        ["مساجد"] = "mosque",
        ["جامع"] = "mosque",
        ["churches"] = "church",
        ["supermarkets"] = "supermarket",
        ["grocery"] = "supermarket",
        ["groceries"] = "supermarket",
        ["malls"] = "mall",
        ["shopping mall"] = "mall",
        ["shopping centre"] = "mall",
        ["shopping center"] = "mall",
        ["banks"] = "bank",
        ["atms"] = "atm",
        ["restaurants"] = "restaurant",
        ["مطاعم"] = "restaurant",
        ["cafes"] = "cafe",
        ["cafés"] = "cafe",
        ["coffee shop"] = "cafe",
        ["parks"] = "park",
        ["حدائق"] = "park",
        ["hotels"] = "hotel",
        ["فنادق"] = "hotel",
        ["petrol station"] = "fuel",
        ["petrol stations"] = "fuel",
        ["gas station"] = "fuel",
        ["gas stations"] = "fuel",
        ["fuel station"] = "fuel",
        ["fuel stations"] = "fuel",
        ["محطات وقود"] = "fuel",
        ["police station"] = "police",
        ["police stations"] = "police",
        ["fire station"] = "fire_station",
        ["fire stations"] = "fire_station",
        ["post office"] = "post_office",
        ["post offices"] = "post_office",
        ["bus station"] = "bus_station",
        ["bus stations"] = "bus_station",
        ["airports"] = "airport",
        ["museums"] = "museum",
        ["libraries"] = "library",
        ["stadiums"] = "stadium",
        ["gyms"] = "gym",
        ["playgrounds"] = "playground",
    };

    private static readonly IReadOnlyDictionary<string, PoiCategoryDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every category this catalog can search, for an honest "not supported" reply.</summary>
    public IReadOnlyList<PoiCategoryDefinition> All => Definitions;

    /// <summary>The category with this stable key, or null — used to reconcile client-reported layers.</summary>
    public PoiCategoryDefinition? FindByKey(string key) =>
        ByKey.TryGetValue(key, out var category) ? category : null;

    /// <summary>
    /// Resolves a user phrase to a category. Returns false for anything outside the catalog —
    /// never guesses, because a wrong tag query answers a different question than the one asked.
    /// </summary>
    public bool TryResolve(string? phrase, out PoiCategoryDefinition category)
    {
        category = null!;
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var normalized = phrase.Trim().TrimEnd('.', '!', '?');

        // An alias wins over a raw key lookup; otherwise try the phrase itself, then its
        // singular form (`schools` → `school`) so a category added to the table needs no
        // second plural alias entry.
        var lookup = Aliases.TryGetValue(normalized, out var aliasKey) ? aliasKey : normalized;
        if (ByKey.TryGetValue(lookup, out category!))
        {
            return true;
        }

        if (normalized.Length > 3 && normalized.EndsWith('s') &&
            ByKey.TryGetValue(normalized[..^1], out category!))
        {
            return true;
        }

        return false;
    }

    /// <summary>The supported keys/names, comma-separated, for the unsupported-category reply.</summary>
    public string DescribeSupported() =>
        string.Join(", ", Definitions.Select(d => d.Key.Replace('_', ' ')));

    private static PoiTagGroup Group(params (string Key, string Value)[] constraints) =>
        new([.. constraints.Select(c => new PoiTagConstraint(c.Key, [c.Value]))]);
}
