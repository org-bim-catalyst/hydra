using MediatR;

namespace AskLucy.Application.Weather.Queries.GetCurrentWeather;

/// <summary>specs/027-immersive-viewer-platform FR-009, contracts/weather-api.md. No persistence (FR-012b) — a pure pass-through lookup.</summary>
public sealed record GetCurrentWeatherQuery(double Latitude, double Longitude) : IRequest<WeatherSnapshotDto>;
