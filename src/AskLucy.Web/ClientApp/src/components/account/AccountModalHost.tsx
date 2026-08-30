import CloseIcon from '@mui/icons-material/Close'
import OpenInFullIcon from '@mui/icons-material/OpenInFull'
import { Box, CircularProgress, Fade, IconButton, Modal, Tooltip, Typography, alpha } from '@mui/material'
import { Suspense } from 'react'
import { useNavigate } from 'react-router'
import { useAccountModalStore } from '../../store/accountModalStore'
import { ModalPageProvider } from './ModalPageContext'
import { MODAL_PAGES } from './accountModalPages'
import { overlaySurface } from '../../theme/tokens/overlaySurface'

/**
 * Renders whichever account destination is open as a modal over the current page.
 *
 * Built to the readdy.ai reference's own modal, read out of its compiled markup rather than
 * judged from a screenshot:
 *
 *   overlay  fixed inset-0 z-50 flex items-start justify-center
 *            bg-foreground-950/40 backdrop-blur-sm animate-fade-in
 *            overflow-y-auto py-6 md:py-10 px-4
 *   panel    max-w-3xl|max-w-6xl w-full bg-background-50 rounded-xl shadow-2xl
 *            border border-background-200/70 animate-scale-in overflow-hidden
 *   header   flex items-center justify-between px-6 md:px-8 py-4 border-b sticky top-0
 *   body     px-6 md:px-8 py-6 md:py-8
 *
 * `items-start` is the detail that matters most and the one I had wrong: the panel sits near
 * the top of the viewport with the page still visible below it, and grows downward with its
 * content. MUI's Dialog centres vertically instead, which is why it read as a different design.
 *
 * A plain `Modal` rather than `Dialog` for the same reason — Dialog owns the centring and the
 * paper chrome, and fighting both is more code than laying out the two elements directly.
 * Modal still supplies the focus trap, the Escape handler, the scroll lock and the portal.
 *
 * Mounted once, by the router's root layout, so both the AppShell pages and the Studio
 * workspace get it without either knowing about it.
 */
export function AccountModalHost() {
  const openPath = useAccountModalStore((s) => s.openPath)
  const close = useAccountModalStore((s) => s.close)
  const navigate = useNavigate()

  const entry = openPath ? MODAL_PAGES[openPath] : undefined
  const open = Boolean(entry)

  const openAsPage = () => {
    const path = openPath
    close()
    if (path) navigate(path)
  }

  return (
    <Modal
      open={open}
      onClose={close}
      aria-labelledby="account-modal-title"
      closeAfterTransition
      slotProps={{
        backdrop: {
          timeout: overlaySurface.enterDurationMs,
          sx: {
            // `bg-foreground-950/40 backdrop-blur-sm`.
            bgcolor: (t) => alpha(t.palette.mode === 'dark' ? '#000000' : '#1a1a17', 0.4),
            backdropFilter: overlaySurface.backdropBlur,
          },
        },
      }}
    >
      <Fade in={open} timeout={overlaySurface.enterDurationMs} easing={overlaySurface.enterEasing}>
        {/* The overlay. Scrolls as one column so a tall page scrolls the modal rather than
            trapping the overflow inside a fixed-height body, and clicking the padding around
            the panel closes — the reference gates that on the click landing on the overlay
            itself, not on a child. */}
        <Box
          onClick={(event) => {
            if (event.target === event.currentTarget) close()
          }}
          sx={{
            position: 'fixed',
            inset: 0,
            display: 'flex',
            alignItems: 'flex-start',
            justifyContent: 'center',
            overflowY: 'auto',
            py: overlaySurface.overlayPaddingY,
            px: overlaySurface.overlayPaddingX,
          }}
        >
          <Box
            sx={{
              width: '100%',
              maxWidth: overlaySurface.modalWidth[entry?.size ?? 'default'],
              bgcolor: 'background.paper',
              backgroundImage: 'none',
              borderRadius: `${overlaySurface.panelRadius}px`,
              boxShadow: overlaySurface.modalShadow,
              border: (t) => `1px solid ${alpha(t.palette.divider, 0.7)}`,
              overflow: 'hidden',
              // `animate-scale-in`: 0.96 → 1 with the fade above, 300ms ease-out.
              animation: `account-modal-scale-in ${overlaySurface.enterDurationMs}ms ${overlaySurface.enterEasing} forwards`,
              '@keyframes account-modal-scale-in': {
                from: { opacity: 0, transform: `scale(${overlaySurface.enterScale})` },
                to: { opacity: 1, transform: 'scale(1)' },
              },
            }}
          >
            <Box
              sx={{
                position: 'sticky',
                top: 0,
                zIndex: 1,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 1,
                px: { xs: 3, md: 4 },
                py: 2,
                bgcolor: 'background.paper',
                borderBottom: (t) => `1px solid ${alpha(t.palette.divider, 0.7)}`,
              }}
            >
              <Typography
                id="account-modal-title"
                component="h2"
                sx={{ fontSize: { xs: '1.125rem', md: '1.25rem' }, fontWeight: 600, letterSpacing: '-0.025em' }}
              >
                {entry?.title}
              </Typography>
              <Box sx={{ display: 'flex', gap: 0.5 }}>
                {/* A modal is a convenience, not a cage: everything here is still a real route,
                    and some of these pages are worth the whole viewport. */}
                <Tooltip title="Open as a full page">
                  <IconButton
                    onClick={openAsPage}
                    aria-label="Open as a full page"
                    sx={{ width: 32, height: 32, borderRadius: `${overlaySurface.controlRadius}px` }}
                  >
                    <OpenInFullIcon sx={{ fontSize: 18 }} />
                  </IconButton>
                </Tooltip>
                <IconButton
                  onClick={close}
                  aria-label="Close"
                  sx={{ width: 32, height: 32, borderRadius: `${overlaySurface.controlRadius}px` }}
                >
                  <CloseIcon sx={{ fontSize: 20 }} />
                </IconButton>
              </Box>
            </Box>

            <Box sx={{ px: { xs: 3, md: 4 }, py: { xs: 3, md: 4 } }}>
              <ModalPageProvider value={true}>
                <Suspense
                  fallback={
                    <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                      <CircularProgress size={28} />
                    </Box>
                  }
                >
                  {entry ? <entry.Component /> : null}
                </Suspense>
              </ModalPageProvider>
            </Box>
          </Box>
        </Box>
      </Fade>
    </Modal>
  )
}
