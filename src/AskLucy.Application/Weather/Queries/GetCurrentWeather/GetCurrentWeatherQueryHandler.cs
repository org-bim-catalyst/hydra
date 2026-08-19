using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Weather.Queries.GetCurrentWeather;

public sealed class GetCurrentWeatherQueryHandler(IWeatherProvider weatherProvider) : IRequestHandler<GetCurrentWeatherQuery, WeatherSnapshotDto>
{
    public Task<WeatherSnapshotDto> Handle(GetCurrentWeatherQuery request, CancellationToken cancellationToken) =>
        weatherProvider.GetCurrentWeatherAsync(request.Latitude, request.Longitude, cancellationToken);
}
