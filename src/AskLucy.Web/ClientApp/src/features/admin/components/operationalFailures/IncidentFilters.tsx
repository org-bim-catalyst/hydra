import { useEffect, useState } from 'react'
import { Autocomplete, FormControl, InputLabel, MenuItem, Select, Stack, TextField } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useIsAdmin } from '../../../../hooks/useIsAdmin'
import { useCan } from '../../../auth/hooks/usePermissions'
import { getUsers } from '../../api/adminApi'
import type { UserAdmin } from '../../api/adminApi'
import type { IncidentStateFilter } from '../../api/adminOperationalFailuresApi'
import { ADMIN_PERMISSIONS } from '../../adminPermissions'
import { TIME_RANGES } from '../../hooks/useIncidentFilterParams'
import type { IncidentFilterParams, IncidentTimeRange } from '../../hooks/useIncidentFilterParams'
import { FAILURE_ENGINES, FAILURE_KINDS, FAILURE_SEVERITIES, engineLabel, kindLabel } from './operationalFailureLabels'

const RANGE_LABELS: Record<IncidentTimeRange, string> = {
  '1h': 'Last hour',
  '24h': 'Last 24 hours',
  '7d': 'Last 7 days',
  '30d': 'Last 30 days',
  custom: 'Custom',
}

const STATE_LABELS: Record<IncidentStateFilter, string> = {
  Unresolved: 'Unresolved',
  Open: 'Open',
  Acknowledged: 'Acknowledged',
  Resolved: 'Resolved',
}

const USER_SEARCH_DELAY_MS = 300
const WEEK_MS = 7 * 24 * 60 * 60 * 1000

/** ISO-8601 ⇄ the `datetime-local` input's local `YYYY-MM-DDTHH:mm`. */
const toLocalInput = (iso: string) => {
  if (!iso) return ''
  const date = new Date(iso)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}
const fromLocalInput = (value: string) => (value ? new Date(value).toISOString() : '')

function MultiSelect<T extends string>({
  label,
  values,
  options,
  optionLabel,
  onChange,
}: {
  label: string
  values: T[]
  options: T[]
  optionLabel: (value: T) => string
  onChange: (values: T[]) => void
}) {
  const id = `incident-filter-${label.toLowerCase()}`
  return (
    <FormControl size="small" sx={{ minWidth: 150 }}>
      <InputLabel id={id}>{label}</InputLabel>
      <Select
        labelId={id}
        label={label}
        multiple
        value={values}
        onChange={(event) => onChange(event.target.value as T[])}
        renderValue={(selected) => selected.map(optionLabel).join(', ')}
      >
        {options.map((option) => (
          <MenuItem key={option} value={option}>
            {optionLabel(option)}
          </MenuItem>
        ))}
      </Select>
    </FormControl>
  )
}

/**
 * Commits on blur or Enter, so typing a provider name does not refetch the list per keystroke.
 * Keyed on the committed value by its parent, so an outside change resets the draft.
 */
function ProviderField({ value, onCommit }: { value: string; onCommit: (value: string) => void }) {
  const [draft, setDraft] = useState(value)

  return (
    <TextField
      size="small"
      label="Provider"
      value={draft}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={() => draft.trim() !== value && onCommit(draft.trim())}
      onKeyDown={(event) => {
        if (event.key === 'Enter') onCommit(draft.trim())
      }}
      sx={{ width: 150 }}
    />
  )
}

/** Searches the admin user list; only rendered for a caller who may view users. */
function UserFilter({
  userId,
  userLabel,
  onChange,
}: {
  userId: string
  userLabel: string
  onChange: (user: { userId: string; userLabel: string }) => void
}) {
  const [input, setInput] = useState(userLabel)
  const [search, setSearch] = useState('')

  useEffect(() => {
    const timer = setTimeout(() => setSearch(input.trim()), USER_SEARCH_DELAY_MS)
    return () => clearTimeout(timer)
  }, [input])

  const usersQuery = useQuery({
    queryKey: ['admin', 'operational-failures', 'user-filter', search],
    queryFn: () => getUsers({ search, pageSize: 10 }),
    enabled: search.length >= 2 && search !== userLabel,
  })

  const selected: UserAdmin | null = userId ? ({ id: userId, email: userLabel || userId } as UserAdmin) : null

  return (
    <Autocomplete
      size="small"
      sx={{ width: 240 }}
      options={usersQuery.data?.items ?? []}
      value={selected}
      inputValue={input}
      onInputChange={(_, next) => setInput(next)}
      onChange={(_, user) => onChange({ userId: user?.id ?? '', userLabel: user?.email ?? '' })}
      getOptionLabel={(user) => user.email}
      isOptionEqualToValue={(option, value) => option.id === value.id}
      filterOptions={(options) => options}
      loading={usersQuery.isFetching}
      noOptionsText={search.length < 2 ? 'Type at least 2 characters' : 'No matching users'}
      renderInput={(params) => (
        <TextField
          {...params}
          label="User"
          error={usersQuery.isError}
          helperText={usersQuery.isError ? 'Could not search users.' : undefined}
        />
      )}
    />
  )
}

/**
 * specs/074 FR-019 — time range, state, severity, engine, kind, provider and user. Values within
 * one multi-select are OR-ed by the API; different filters are AND-ed.
 */
export function IncidentFilters({
  params,
  onChange,
}: {
  params: IncidentFilterParams
  onChange: (patch: Partial<IncidentFilterParams>) => void
}) {
  const isBuiltInAdmin = useIsAdmin()
  const canViewUsers = useCan(ADMIN_PERMISSIONS.usersView)
  const customInvalid = params.range === 'custom' && params.from !== '' && params.to !== '' && params.from > params.to

  return (
    <Stack direction="row" spacing={1.5} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'flex-start', mb: 2 }} role="group" aria-label="Incident filters">
      <FormControl size="small" sx={{ minWidth: 150 }}>
        <InputLabel id="incident-filter-range">Time range</InputLabel>
        <Select
          labelId="incident-filter-range"
          label="Time range"
          value={params.range}
          onChange={(event) => {
            const range = event.target.value as IncidentTimeRange
            onChange(
              range === 'custom'
                ? { range, from: params.from || new Date(Date.now() - WEEK_MS).toISOString(), to: params.to || new Date().toISOString() }
                : { range },
            )
          }}
        >
          {TIME_RANGES.map((range) => (
            <MenuItem key={range} value={range}>
              {RANGE_LABELS[range]}
            </MenuItem>
          ))}
        </Select>
      </FormControl>
      {params.range === 'custom' && (
        <>
          <TextField
            size="small"
            type="datetime-local"
            label="From"
            value={toLocalInput(params.from)}
            onChange={(event) => onChange({ from: fromLocalInput(event.target.value) })}
            slotProps={{ inputLabel: { shrink: true } }}
            error={customInvalid}
            helperText={customInvalid ? 'From must be before To.' : undefined}
          />
          <TextField
            size="small"
            type="datetime-local"
            label="To"
            value={toLocalInput(params.to)}
            onChange={(event) => onChange({ to: fromLocalInput(event.target.value) })}
            slotProps={{ inputLabel: { shrink: true } }}
            error={customInvalid}
          />
        </>
      )}
      <FormControl size="small" sx={{ minWidth: 140 }}>
        <InputLabel id="incident-filter-state">State</InputLabel>
        <Select
          labelId="incident-filter-state"
          label="State"
          value={params.state}
          onChange={(event) => onChange({ state: event.target.value as IncidentStateFilter })}
        >
          {(Object.keys(STATE_LABELS) as IncidentStateFilter[]).map((state) => (
            <MenuItem key={state} value={state}>
              {STATE_LABELS[state]}
            </MenuItem>
          ))}
        </Select>
      </FormControl>
      <MultiSelect
        label="Severity"
        values={params.severity}
        options={FAILURE_SEVERITIES}
        optionLabel={(severity) => severity}
        onChange={(severity) => onChange({ severity })}
      />
      <MultiSelect
        label="Engine"
        values={params.engine}
        options={FAILURE_ENGINES}
        optionLabel={engineLabel}
        onChange={(engine) => onChange({ engine })}
      />
      <MultiSelect label="Kind" values={params.kind} options={FAILURE_KINDS} optionLabel={kindLabel} onChange={(kind) => onChange({ kind })} />
      <ProviderField key={params.provider} value={params.provider} onCommit={(provider) => onChange({ provider })} />
      {(isBuiltInAdmin || canViewUsers) && (
        <UserFilter userId={params.userId} userLabel={params.userLabel} onChange={(user) => onChange(user)} />
      )}
    </Stack>
  )
}
