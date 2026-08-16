import { Stack, TextField, Typography } from '@mui/material'
import type { TextFieldProps } from '@mui/material'
import type { ReactNode } from 'react'
import { flumeriaColor } from '../features/landing/theme/flumeriaPalette'

interface FormFieldProps extends Omit<TextFieldProps, 'label' | 'id'> {
  id: string
  label: string
  /** Right-aligned element next to the label, e.g. a "Forgot password?" link. */
  action?: ReactNode
}

/**
 * Auth-page form field matching the Readdy.ai reference exactly: a static label above the
 * field plus a plain placeholder inside — not MUI's TextField `label` prop, which floats
 * the label inside the border and (critically) never learns a browser autofilled the field
 * without a React change event, leaving it overlapping the value (research.md Topic 3).
 */
export function FormField({ id, label, action, ...textFieldProps }: FormFieldProps) {
  return (
    <Stack spacing={1}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography
          component="label"
          htmlFor={id}
          variant="body2"
          sx={{ fontWeight: 600, color: flumeriaColor.heading }}
        >
          {label}
        </Typography>
        {action}
      </Stack>
      <TextField id={id} fullWidth {...textFieldProps} />
    </Stack>
  )
}
