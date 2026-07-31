import BrightnessMediumIcon from '@mui/icons-material/Brightness4'
import { Box, IconButton, Stack, Typography, useTheme } from '@mui/material'
import { BrandMark } from '../../../components/BrandMark'
import { UserMenu } from '../../../components/UserMenu'
import { useThemeStore } from '../../../store/themeStore'

/** FR-015: the minimal navigation retained outside the floating assistant panel — brand
 * identity, theme, and account access. Everything chat-specific (language, translate,
 * generate image, message list, composer) lives inside AssistantPanel instead (T015).
 * Transparent/pointer-events-none outside its controls so the 3D scene stays draggable
 * through the empty space of the bar. */
export function MinimalTopBar() {
  const theme = useTheme()
  const toggleTheme = useThemeStore((s) => s.toggle)

  return (
    <Box
      component="header"
      sx={{
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        zIndex: 2,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        px: 2,
        height: 56,
        pointerEvents: 'none',
        '& > *': { pointerEvents: 'auto' },
      }}
    >
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <BrandMark size={24} color={theme.palette.primary.main} />
        {/* FR-010/FR-015: dropped on narrow viewports first — the icon alone still
            identifies the brand, and the extra width matters more on mobile. */}
        <Typography
          variant="subtitle1"
          sx={{ fontWeight: 600, display: { xs: 'none', sm: 'block' } }}
        >
          Ask Lucy
        </Typography>
      </Stack>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <IconButton onClick={toggleTheme} aria-label="Toggle theme">
          <BrightnessMediumIcon />
        </IconButton>
        <UserMenu />
      </Stack>
    </Box>
  )
}
