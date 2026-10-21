using System.Text.Json.Serialization;

namespace AskLucy.Application.MapCaptures;

public enum MapCaptureMode
{
    [JsonStringEnumMemberName("roadmap")]
    Roadmap,

    [JsonStringEnumMemberName("satellite")]
    Satellite,

    [JsonStringEnumMemberName("hybrid")]
    Hybrid,

    [JsonStringEnumMemberName("buildings-only")]
    BuildingsOnly,
}
