import { Skeleton, Stack } from '@mui/material'
import { Fragment } from 'react'

interface SkeletonBlockProps {
  variant: 'text' | 'card' | 'row'
  count?: number
}

const VARIANT_HEIGHT: Record<SkeletonBlockProps['variant'], number | undefined> = {
  text: undefined,
  card: 120,
  row: 56,
}

/** Shared loading placeholder (FR-008), replacing ad hoc per-feature `Skeleton` usage so
 * every list/panel's loading state reads the same way. */
export function SkeletonBlock({ variant, count = 1 }: SkeletonBlockProps) {
  const shape = variant === 'text' ? 'text' : 'rounded'
  const height = VARIANT_HEIGHT[variant]

  return (
    <Stack spacing={1} aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <Fragment key={index}>
          <Skeleton variant={shape} height={height} sx={{ borderRadius: variant === 'text' ? undefined : 2 }} />
        </Fragment>
      ))}
    </Stack>
  )
}
