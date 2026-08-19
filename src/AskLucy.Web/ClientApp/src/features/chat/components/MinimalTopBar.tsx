import BrightnessMediumIcon from '@mui/icons-material/Brightness4'
import { Box, IconButton, Stack, Typography, useTheme } from '@mui/material'
import { BrandMark } from '../../../components/BrandMark'
import { UserMenu } from '../../../components/UserMenu'
import { useThemeStore } from '../../../store/themeStore'
import { createGlassTokens } from '../../../theme/tokens/glass'
import { radius } from '../../../theme'

/** FR-015: the minimal navigation retained outside the floating assistant panel — brand
 * identity, theme, and account access. Everything chat-specific (language, translate,
 * generate image, message list, composer) lives inside AssistantPanel instead (T015).
 * Transparent/pointer-events-none outside its controls so the 3D scene stays draggable
 * through the empty space of the bar. */
export function MinimalTopBar() {
  const theme = useTheme()
  const toggleTheme = useThemeStore((s) => s.toggle)
  const glass = createGlassTokens(theme.palette.mode)

  // A translucent backdrop only behind each control cluster (not the full bar) keeps
  // brand/theme/account legible over the moving particle-sphere scene without blocking
  // the empty space in between from remaining draggable (research.md #3).
  const clusterSx = {
    alignItems: 'center',
    px: 1,
    py: 0.5,
    borderRadius: `${radius.pill}px`,
    bgcolor: glass.background,
    backdropFilter: glass.backdropFilter,
    border: `1px solid ${glass.border}`,
  } as const

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
        py: 1,
        height: 56,
        boxSizing: 'border-box',
        pointerEvents: 'none',
        '& > *': { pointerEvents: 'auto' },
      }}
    >
      <Stack direction="row" spacing={1} sx={clusterSx}>
        <BrandMark size={24} color={theme.palette.primary.main} />
        {/* FR-010/FR-015: dropped on narrow viewports first — the icon alone still
            identifies the brand, and the extra width matters more on mobile. */}
        <Typography
          variant="subtitle1"
          sx={{ display: { xs: 'none', sm: 'block' }, fontWeight: 600, lineHeight: 1.2 }}
        >
          Flumeria
        </Typography>
      </Stack>
      <Stack direction="row" spacing={0.5} sx={clusterSx}>
        <IconButton onClick={toggleTheme} aria-label="Toggle theme">
          <BrightnessMediumIcon />
        </IconButton>
        <UserMenu />
      </Stack>
    </Box>
  )
}
