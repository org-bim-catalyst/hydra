import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import type {
  FailureEngine,
  FailureKind,
  FailureSeverity,
  IncidentFilters,
  IncidentStateFilter,
} from '../api/adminOperationalFailuresApi'
import { FAILURE_ENGINES, FAILURE_KINDS, FAILURE_SEVERITIES } from '../components/operationalFailures/operationalFailureLabels'

export type IncidentTimeRange = '1h' | '24h' | '7d' | '30d' | 'custom'

const RANGE_MS: Record<Exclude<IncidentTimeRange, 'custom'>, number> = {
  '1h': 60 * 60 * 1000,
  '24h': 24 * 60 * 60 * 1000,
  '7d': 7 * 24 * 60 * 60 * 1000,
  '30d': 30 * 24 * 60 * 60 * 1000,
}

export const TIME_RANGES = Object.keys(RANGE_MS).concat('custom') as IncidentTimeRange[]

const STATES: IncidentStateFilter[] = ['Unresolved', 'Open', 'Acknowledged', 'Resolved']

/** What the filter bar edits; everything lives in the URL, so a filtered view survives reload and can be shared. */
export interface IncidentFilterParams {
  range: IncidentTimeRange
  /** ISO-8601; only read when `range` is `custom`. */
  from: string
  to: string
  state: IncidentStateFilter
  severity: FailureSeverity[]
  engine: FailureEngine[]
  kind: FailureKind[]
  provider: string
  userId: string
  /** The selected user's email, kept beside the id so the chip reads right after a reload. */
  userLabel: string
}

// A hand-edited URL with an unknown value drops it rather than sending the API a 400.
const known = <T extends string>(values: string[], allowed: readonly T[]) =>
  values.filter((value): value is T => (allowed as readonly string[]).includes(value))

/**
 * specs/074 FR-019 — the incident list's filters, read from and written to the URL. A preset's
 * start is computed once per preset choice, not per render, so paging never slides the window
 * under the administrator.
 */
export function useIncidentFilterParams() {
  const [searchParams, setSearchParams] = useSearchParams()
  // The instant a preset counts back from: when the page opened, or when a preset was last chosen.
  const [anchorMs, setAnchorMs] = useState(() => Date.now())

  const rangeParam = searchParams.get('range')
  const range: IncidentTimeRange = (TIME_RANGES as string[]).includes(rangeParam ?? '') ? (rangeParam as IncidentTimeRange) : '7d'
  const stateParam = searchParams.get('state')

  const params: IncidentFilterParams = {
    range,
    from: searchParams.get('from') ?? '',
    to: searchParams.get('to') ?? '',
    state: known([stateParam ?? ''], STATES)[0] ?? 'Unresolved',
    severity: known(searchParams.getAll('severity'), FAILURE_SEVERITIES),
    engine: known(searchParams.getAll('engine'), FAILURE_ENGINES),
    kind: known(searchParams.getAll('kind'), FAILURE_KINDS),
    provider: searchParams.get('provider') ?? '',
    userId: searchParams.get('user') ?? '',
    userLabel: searchParams.get('userLabel') ?? '',
  }

  const customFrom = params.from
  const from = useMemo(
    () =>
      range === 'custom' && customFrom
        ? customFrom
        : new Date(anchorMs - RANGE_MS[range === 'custom' ? '7d' : range]).toISOString(),
    [range, customFrom, anchorMs],
  )

  const filters: Omit<IncidentFilters, 'page' | 'pageSize'> = {
    from,
    to: range === 'custom' && params.to ? params.to : undefined,
    state: params.state,
    severity: params.severity,
    engine: params.engine,
    kind: params.kind,
    provider: params.provider || undefined,
    userId: params.userId || undefined,
  }

  const update = (patch: Partial<IncidentFilterParams>) => {
    const next = { ...params, ...patch }
    if (patch.range !== undefined) setAnchorMs(Date.now())
    const query = new URLSearchParams()
    if (next.range !== '7d') query.set('range', next.range)
    if (next.range === 'custom') {
      if (next.from) query.set('from', next.from)
      if (next.to) query.set('to', next.to)
    }
    if (next.state !== 'Unresolved') query.set('state', next.state)
    for (const value of next.severity) query.append('severity', value)
    for (const value of next.engine) query.append('engine', value)
    for (const value of next.kind) query.append('kind', value)
    if (next.provider) query.set('provider', next.provider)
    if (next.userId) {
      query.set('user', next.userId)
      if (next.userLabel) query.set('userLabel', next.userLabel)
    }
    setSearchParams(query, { replace: true })
  }

  return { params, filters, update }
}
