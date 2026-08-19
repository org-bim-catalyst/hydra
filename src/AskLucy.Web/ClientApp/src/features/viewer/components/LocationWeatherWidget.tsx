import { Box, Stack, Typography } from '@mui/material'
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
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'
import { useCurrentWeather } from '../hooks/useCurrentWeather'
import type { WeatherCondition } from '../api/weatherApi'

function conditionIcon(condition: WeatherCondition, isDaytime: boolean): ReactNode {
  switch (condition) {
    case 'Clear':
      return isDaytime ? <RiSunLine size={28} /> : <RiMoonClearLine size={28} />
    case 'PartlyCloudy':
      return isDaytime ? <RiSunCloudyLine size={28} /> : <RiMoonCloudyLine size={28} />
    case 'Cloudy':
      return <RiCloudyLine size={28} />
    case 'Fog':
      return <RiFoggyLine size={28} />
    case 'Rain':
      return <RiRainyLine size={28} />
    case 'Snow':
      return <RiSnowyLine size={28} />
    case 'Thunderstorm':
      return <RiThunderstormsLine size={28} />
    case 'Windy':
      return <RiWindyLine size={28} />
  }
}

export interface LocationWeatherWidgetProps {
  latitude: number | null
  longitude: number | null
}

/** FR-009/FR-010/FR-011: a compact, glanceable readout of the resolved location's name,
 * temperature, and condition icon, styled to match `CircularAction`'s dark-glass chrome so it
 * reads as part of the same workspace-shell control family. Renders nothing while location
 * hasn't resolved (FR-008), and nothing on a first-attempt failure with no prior reading —
 * `useCurrentWeather`'s `isStale` flag covers "shows a clearly indicated stale reading instead
 * of going blank" for a *later* failure once one has already loaded. */
export function LocationWeatherWidget({ latitude, longitude }: LocationWeatherWidgetProps) {
  const { data, isStale } = useCurrentWeather(latitude, longitude)

  // FR-008/FR-012: no current location means no widget, full stop — regardless of whatever a
  // prior location's cached reading (`placeholderData`, see useCurrentWeather.ts) might still
  // be holding onto, which exists to survive a same-location refetch *failure* (FR-011), not
  // to keep showing a reading for a location that's no longer active.
  if (latitude === null || longitude === null) return null
  if (!data) return null

  return (
    <Box
      role="status"
      aria-label={`Weather in ${data.locationName}: ${Math.round(data.temperatureCelsius)}°C, ${data.condition}${isStale ? ' (last known reading)' : ''}`}
      sx={{
        position: 'absolute',
        // Below HomeProjectCard (features/chat/components/HomeProjectCard.tsx), which already
        // occupies top: {16, 20} / left: {16, 20} — stacking here instead of overlapping it.
        top: { xs: 76, sm: 84 },
        left: { xs: 16, sm: 24 },
        pointerEvents: 'none',
        borderRadius: 2,
        px: 2,
        py: 1.25,
        bgcolor: CIRCULAR_ACTION_CHROME.expandedBg,
        border: CIRCULAR_ACTION_CHROME.border,
        backdropFilter: 'blur(12px)',
        color: CIRCULAR_ACTION_CHROME.icon,
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
      }}
    >
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
        {conditionIcon(data.condition, data.isDaytime)}
        <Box>
          <Typography variant="subtitle2" component="div" sx={{ lineHeight: 1.2 }}>
            {data.locationName}
          </Typography>
          <Typography variant="h6" component="div" sx={{ lineHeight: 1.2 }}>
            {Math.round(data.temperatureCelsius)}°C
          </Typography>
        </Box>
      </Stack>
      {isStale && (
        <Typography variant="caption" component="div" sx={{ opacity: 0.75, mt: 0.5 }}>
          Last known reading
        </Typography>
      )}
    </Box>
  )
}
