import { apiFetch } from '../../../api/httpClient'

/** contracts/weather-api.md — `WeatherCondition` is a closed set the backend maps the upstream
 * provider's raw codes into (research.md Decision 7); kept in sync with `WeatherCondition.cs`. */
export type WeatherCondition =
  | 'Clear'
  | 'PartlyCloudy'
  | 'Cloudy'
  | 'Fog'
  | 'Rain'
  | 'Snow'
  | 'Thunderstorm'
  | 'Windy'

export interface WeatherSnapshot {
  locationName: string
  temperatureCelsius: number
  condition: WeatherCondition
  isDaytime: boolean
  observedAtUtc: string
}

export const getCurrentWeather = (latitude: number, longitude: number) =>
  apiFetch<WeatherSnapshot>(`/weather/current?latitude=${latitude}&longitude=${longitude}`)
