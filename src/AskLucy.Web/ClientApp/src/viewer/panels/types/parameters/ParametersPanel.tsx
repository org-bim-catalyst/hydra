import { Button, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { panelTypeRegistry } from '../../registry'

/** contracts/panel-type-registry.md "parameters" built-in type — covers the spec's "Parameters
 * and controls" category. `resizable: false` (spec Assumption: simple fixed-content panel types
 * may be fixed-size by design) exercises FR-005/US2-AS3's "no resize handles shown" path. */
export const parametersDataSchema = z.object({
  fields: z
    .array(
      z.object({
        key: z.string(),
        label: z.string(),
        type: z.enum(['text', 'number', 'select']),
        value: z.union([z.string(), z.number()]),
        options: z.array(z.string()).optional(),
      }),
    )
    .min(1),
})

export type ParametersData = z.infer<typeof parametersDataSchema>

function ParametersPanelRenderer({ data }: { data: ParametersData }) {
  const defaultValues = Object.fromEntries(data.fields.map((field) => [field.key, field.value]))
  const { register, handleSubmit } = useForm<Record<string, string | number>>({ defaultValues })

  // No submission endpoint exists yet for AI-requested parameter changes (out of this feature's
  // scope, spec Assumption) — Apply is a local-only affordance proving the control surface
  // renders and is interactive; a future feature wires this to an actual command.
  const onApply = handleSubmit(() => undefined)

  return (
    <Stack component="form" onSubmit={onApply} spacing={2}>
      {data.fields.map((field) =>
        field.type === 'select' ? (
          <TextField key={field.key} select label={field.label} defaultValue={field.value} {...register(field.key)}>
            {(field.options ?? []).map((option) => (
              <MenuItem key={option} value={option}>
                {option}
              </MenuItem>
            ))}
          </TextField>
        ) : (
          <TextField
            key={field.key}
            label={field.label}
            type={field.type === 'number' ? 'number' : 'text'}
            {...register(field.key)}
          />
        ),
      )}
      <Button type="submit" variant="outlined" size="small" sx={{ alignSelf: 'flex-start' }}>
        Apply
      </Button>
      {data.fields.length === 0 && (
        <Typography variant="body2" color="text.secondary">
          No parameters to show.
        </Typography>
      )}
    </Stack>
  )
}

panelTypeRegistry.register({
  typeKey: 'parameters',
  renderer: ParametersPanelRenderer,
  schema: parametersDataSchema,
  defaultSize: { width: 360, height: 320 },
  resizable: false,
})
