import { Box, Typography } from '@mui/material'
import {
  RiCloudyLine,
  RiFoggyLine,
  RiMoonClearLine,
  RiMoonCloudyLine,
  RiRainyLine,
  RiSnowyLine,
  RiSunCloudyLine,
  RiSunLine,
  RiThunderstormsLine,
  RiWindyLine,
} from '@remixicon/react'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import { HudCard } from '../../../components/workspace-shell/HudCard'
import { useCurrentWeather } from '../hooks/useCurrentWeather'
import type { WeatherCondition } from '../api/weatherApi'
import { useActiveLocationStore } from '../../../store/activeLocationStore'

/** Sized to sit beside two text lines inside the 40 px HUD card (specs/073 research D5). */
const ICON_SIZE = 20

/** Everything the widget says, in one place. */
const COPY = {
  unavailable: 'Weather unavailable',
  unavailableLabel: 'Weather is unavailable',
  staleMarker: '· last known',
  readingLabel: (locationName: string, temperature: number, condition: WeatherCondition, isStale: boolean) =>
    `Weather in ${locationName}: ${temperature}°C, ${condition}${isStale ? ' (last known reading)' : ''}`,
}

function conditionIcon(condition: WeatherCondition, isDaytime: boolean): ReactNode {
  switch (condition) {
    case 'Clear':
      return isDaytime ? <RiSunLine size={ICON_SIZE} /> : <RiMoonClearLine size={ICON_SIZE} />
    case 'PartlyCloudy':
      return isDaytime ? <RiSunCloudyLine size={ICON_SIZE} /> : <RiMoonCloudyLine size={ICON_SIZE} />
    case 'Cloudy':
      return <RiCloudyLine size={ICON_SIZE} />
    case 'Fog':
      return <RiFoggyLine size={ICON_SIZE} />
    case 'Rain':
      return <RiRainyLine size={ICON_SIZE} />
    case 'Snow':
      return <RiSnowyLine size={ICON_SIZE} />
    case 'Thunderstorm':
      return <RiThunderstormsLine size={ICON_SIZE} />
    case 'Windy':
      return <RiWindyLine size={ICON_SIZE} />
  }
}

/** FR-009/FR-010/FR-011: a compact, glanceable readout of the resolved location's name,
 * temperature, and condition icon. Renders nothing while location hasn't resolved (FR-008), and
 * nothing while a first lookup is still in flight — `useCurrentWeather`'s `isStale` flag covers
 * "shows a clearly indicated stale reading instead of going blank" for a *later* failure once one
 * has already loaded.
 *
 * specs/073: one item of the studio's top-left HUD row, on the shared 40 px `HudCard` surface —
 * the row positions it and reserves its space. Two lines fit that height (research D5): the
 * location name, then the temperature, with a stale reading marked inline on the temperature line
 * rather than on a third line of its own. */
export function LocationWeatherWidget() {
  // specs/036-startup-geolocation: reads coordinates from the shared store rather than props,
  // so both startup geolocation and agent-confirmed locations drive the same widget.
  const latitude = useActiveLocationStore((s) => s.latitude)
  const longitude = useActiveLocationStore((s) => s.longitude)
  const setLocationName = useActiveLocationStore((s) => s.setLocationName)
  const { data, isStale, isError } = useCurrentWeather(latitude, longitude)

  // FR-008/SC-005: when weather data arrives, push the resolved locationName back into the
  // shared store so that any other consumer (e.g., the agent context block) sees the same name.
  // Falls back to "${lat}, ${lon}" when the API returns no name (SC-005).
  useEffect(() => {
    if (data && latitude !== null && longitude !== null) {
      const name = data.locationName || `${latitude}, ${longitude}`
      setLocationName(latitude, longitude, name)
    }
  }, [data, latitude, longitude, setLocationName])

  // FR-008/FR-012: no current location means no widget, full stop.
  if (latitude === null || longitude === null) return null

  // A failed lookup used to render nothing at all, so the widget simply vanished and the user
  // was told nothing — the constitution's no-silent-failures rule applies to a decorative widget
  // too. On 2026-09-01 the weather endpoint 502'd twice and this was the entire visible effect.
  // Kept deliberately quiet: a line in the widget's own place, not a toast or a banner, because
  // the failure costs the user nothing else.
  if (!data) {
    if (!isError) return null

    return (
      <HudCard role="status" aria-label={COPY.unavailableLabel} sx={{ opacity: 0.75 }}>
        <Typography variant="subtitle2" component="div" noWrap sx={{ lineHeight: 1.25 }}>
          {COPY.unavailable}
        </Typography>
      </HudCard>
    )
  }

  const temperature = Math.round(data.temperatureCelsius)

  return (
    <HudCard
      role="status"
      aria-label={COPY.readingLabel(data.locationName, temperature, data.condition, isStale)}
      maxWidth={240}
      sx={{ gap: 1.25 }}
    >
      <Box sx={{ display: 'flex', flexShrink: 0 }}>{conditionIcon(data.condition, data.isDaytime)}</Box>
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="caption" component="div" noWrap sx={{ lineHeight: 1.25, opacity: 0.8 }}>
          {data.locationName}
        </Typography>
        <Typography variant="subtitle2" component="div" noWrap sx={{ lineHeight: 1.25, fontWeight: 600 }}>
          {temperature}°C
          {isStale && (
            <Typography variant="caption" component="span" sx={{ ml: 0.75, opacity: 0.75, fontWeight: 400 }}>
              {COPY.staleMarker}
            </Typography>
          )}
        </Typography>
      </Box>
    </HudCard>
  )
}
