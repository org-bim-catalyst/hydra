using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Weather;

/// <summary>contracts/weather-api.md — the <c>GET /api/v1/weather/current</c> response shape.</summary>
public sealed record WeatherSnapshotDto(string LocationName, double TemperatureCelsius, WeatherCondition Condition, bool IsDaytime, DateTimeOffset ObservedAtUtc);
