import { Box, Button, Stack, TextField, Typography } from '@mui/material'
import { useState, type FormEvent } from 'react'
import { newsletter } from '../content/copy'
import { flumeriaColor, flumeriaRadius } from '../theme/flumeriaPalette'

/**
 * Black newsletter band, matching the reference design. Presentational only — no backend
 * email-capture capability exists in this spec (FR-002 doesn't call for one), so submitting
 * shows a local, client-only confirmation rather than silently doing nothing (which would
 * read as broken) or claiming a persisted subscription that doesn't exist.
 */
export function NewsletterSection() {
  const [submitted, setSubmitted] = useState(false)

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    setSubmitted(true)
  }

  return (
    <Box component="section" aria-label="Stay informed" sx={{ bgcolor: flumeriaColor.black, px: { xs: 3, sm: 6, md: 10 }, py: { xs: 8, md: 10 } }}>
      <Stack spacing={2.5} sx={{ maxWidth: 560, mx: 'auto', textAlign: 'center' }}>
        <Typography variant="h4" sx={{ color: flumeriaColor.white, fontWeight: 800 }}>
          {newsletter.title}
        </Typography>
        <Typography variant="body1" sx={{ color: flumeriaColor.bodyOnDark }}>
          {newsletter.body}
        </Typography>
        {submitted ? (
          <Typography variant="body1" sx={{ color: flumeriaColor.greenLight, fontWeight: 600 }}>
            {newsletter.confirmation}
          </Typography>
        ) : (
          <Stack
            component="form"
            onSubmit={handleSubmit}
            direction={{ xs: 'column', sm: 'row' }}
            spacing={1.5}
            sx={{ justifyContent: 'center' }}
          >
            <TextField
              type="email"
              required
              placeholder={newsletter.placeholder}
              aria-label={newsletter.placeholder}
              size="small"
              sx={{
                minWidth: { sm: 320 },
                '& .MuiOutlinedInput-root': {
                  bgcolor: 'rgba(255,255,255,0.08)',
                  color: flumeriaColor.white,
                  borderRadius: `${flumeriaRadius.pill}px`,
                  '& fieldset': { borderColor: 'rgba(255,255,255,0.2)' },
                },
                '& .MuiInputBase-input::placeholder': { color: 'rgba(255,255,255,0.5)', opacity: 1 },
              }}
            />
            <Button
              type="submit"
              variant="contained"
              sx={{
                bgcolor: flumeriaColor.green,
                borderRadius: `${flumeriaRadius.pill}px`,
                px: 3,
                '&:hover': { bgcolor: flumeriaColor.greenDark },
              }}
            >
              {newsletter.cta}
            </Button>
          </Stack>
        )}
      </Stack>
    </Box>
  )
}
