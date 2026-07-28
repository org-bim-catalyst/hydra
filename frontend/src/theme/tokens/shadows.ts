import type { Shadows } from '@mui/material/styles'

function subtleShadow(elevation: number, isDark: boolean): string {
  if (elevation === 0) return 'none'

  const alpha = isDark ? 0.45 : 0.08
  const y = Math.min(1 + elevation * 0.6, 24)
  const blur = Math.min(2 + elevation * 1.4, 48)
  return `0 ${y}px ${blur}px rgba(0, 0, 0, ${alpha})`
}

export function createShadows(isDark: boolean): Shadows {
  return Array.from({ length: 25 }, (_, elevation) => subtleShadow(elevation, isDark)) as unknown as Shadows
}
