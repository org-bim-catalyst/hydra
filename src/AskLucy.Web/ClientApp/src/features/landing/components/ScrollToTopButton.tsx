import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward'
import { Fade, IconButton } from '@mui/material'
import { useEffect, useState } from 'react'
import { flumeriaColor } from '../theme/flumeriaPalette'

const SHOW_AFTER_PX = 480

/** Floating "back to top" button, shown once the visitor has scrolled past the hero. */
export function ScrollToTopButton() {
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    const onScroll = () => setVisible(window.scrollY > SHOW_AFTER_PX)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  return (
    <Fade in={visible}>
      <IconButton
        onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
        aria-label="Back to top"
        sx={{
          position: 'fixed',
          right: { xs: 16, md: 32 },
          // Offset well clear of the footer's bottom-right copyright line (LandingFooter is
          // also right-aligned there) so the button never sits on top of it at page end.
          bottom: { xs: 96, md: 104 },
          zIndex: (theme) => theme.zIndex.speedDial,
          bgcolor: flumeriaColor.green,
          color: flumeriaColor.white,
          boxShadow: 3,
          '&:hover': { bgcolor: flumeriaColor.greenDark },
        }}
      >
        <ArrowUpwardIcon />
      </IconButton>
    </Fade>
  )
}
