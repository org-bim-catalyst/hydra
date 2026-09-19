import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import RadioButtonUncheckedRoundedIcon from '@mui/icons-material/RadioButtonUncheckedRounded'
import { Box, Stack, Typography } from '@mui/material'
import { flumeriaColor } from '../../landing/theme/flumeriaPalette'
import { evaluatePassword } from '../passwordPolicy'

interface PasswordRequirementsProps {
  password: string
  /** Ties the checklist to its field for screen readers via the field's `aria-describedby`. */
  id: string
}

const SEGMENT_COLORS = ['#DC2626', '#D97706', '#65A30D', flumeriaColor.green] as const
const STRENGTH_LABELS = { weak: 'Weak', fair: 'Fair', good: 'Good', strong: 'Strong' } as const

/**
 * The password policy as a live checklist plus a four-segment strength bar.
 *
 * Replaces a single sentence listing every rule, which was ambiguous in exactly the way that
 * matters: the user could not tell whether it was advice or a complaint about what they had just
 * typed. Ticking each rule off as it is satisfied answers that without a submit.
 */
export function PasswordRequirements({ password, id }: PasswordRequirementsProps) {
  const { score, label, rules } = evaluatePassword(password)

  return (
    <Box id={id} sx={{ mt: 1.5 }}>
      <Stack direction="row" spacing={0.75} sx={{ mb: 1 }} aria-hidden="true">
        {[0, 1, 2, 3].map((segment) => (
          <Box
            key={segment}
            sx={{
              height: 4,
              flex: 1,
              borderRadius: 2,
              bgcolor: segment < score ? SEGMENT_COLORS[score - 1] : flumeriaColor.border,
              transition: 'background-color 150ms ease',
            }}
          />
        ))}
      </Stack>

      {/* The bar is decorative; this line is what assistive technology announces. `polite`
          rather than `assertive` so it does not interrupt the user mid-keystroke. */}
      <Typography
        variant="caption"
        aria-live="polite"
        sx={{ display: 'block', mb: 1, color: flumeriaColor.body, fontWeight: 600 }}
      >
        {password.length === 0 ? 'Password strength' : `Password strength: ${STRENGTH_LABELS[label]}`}
      </Typography>

      <Stack component="ul" spacing={0.25} sx={{ listStyle: 'none', m: 0, p: 0 }}>
        {rules.map((rule) => (
          <Stack
            key={rule.id}
            component="li"
            direction="row"
            spacing={0.75}
            sx={{ alignItems: 'center' }}
          >
            {rule.met ? (
              <CheckCircleRoundedIcon sx={{ fontSize: 16, color: flumeriaColor.green }} />
            ) : (
              <RadioButtonUncheckedRoundedIcon sx={{ fontSize: 16, color: flumeriaColor.border }} />
            )}
            <Typography
              variant="caption"
              sx={{ color: rule.met ? flumeriaColor.greenLightText : flumeriaColor.body }}
            >
              {rule.label}
            </Typography>
            {/* The icon carries the state visually; this carries it to a screen reader without
                repeating the rule text in the accessible name of every row. */}
            <Box component="span" sx={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
              {rule.met ? ' — met' : ' — not met'}
            </Box>
          </Stack>
        ))}
      </Stack>
    </Box>
  )
}
