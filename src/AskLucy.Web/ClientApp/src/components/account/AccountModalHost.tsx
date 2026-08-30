import CloseIcon from '@mui/icons-material/Close'
import OpenInFullIcon from '@mui/icons-material/OpenInFull'
import {
  Box,
  CircularProgress,
  Dialog,
  DialogContent,
  DialogTitle,
  IconButton,
  Tooltip,
  Typography,
} from '@mui/material'
import { Suspense } from 'react'
import { useNavigate } from 'react-router'
import { useAccountModalStore } from '../../store/accountModalStore'
import { ModalPageProvider } from './ModalPageContext'
import { MODAL_PAGES } from './accountModalPages'
import { radius } from '../../theme'

/**
 * Renders whichever account destination is open as a dialog over the current page.
 *
 * Mounted once, by the router's root layout, so both the AppShell pages and the Studio
 * workspace get it without either knowing about it.
 */
export function AccountModalHost() {
  const openPath = useAccountModalStore((s) => s.openPath)
  const close = useAccountModalStore((s) => s.close)
  const navigate = useNavigate()

  const entry = openPath ? MODAL_PAGES[openPath] : undefined

  const openAsPage = () => {
    const path = openPath
    close()
    if (path) navigate(path)
  }

  return (
    <Dialog
      open={Boolean(entry)}
      onClose={close}
      maxWidth="lg"
      fullWidth
      scroll="paper"
      aria-labelledby="account-modal-title"
      slotProps={{
        paper: {
          sx: {
            // Theme tokens throughout, never a hardcoded panel colour — these open over both
            // the light landing page and the dark Studio workspace.
            bgcolor: 'background.paper',
            backgroundImage: 'none',
            borderRadius: radius.lg,
            border: (t) => `1px solid ${t.palette.mode === 'dark' ? 'rgba(255,255,255,0.10)' : 'rgba(0,0,0,0.10)'}`,
            maxHeight: '86dvh',
          },
        },
      }}
    >
      <DialogTitle
        id="account-modal-title"
        sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1, py: 1.5 }}
      >
        <Typography variant="subtitle1" component="span" sx={{ fontWeight: 600 }}>
          {entry?.title}
        </Typography>
        <Box sx={{ display: 'flex', gap: 0.5 }}>
          {/* A modal is a convenience, not a cage: anything reachable here is still a real
              route, and some of these pages are worth the whole viewport. */}
          <Tooltip title="Open as a full page">
            <IconButton onClick={openAsPage} size="small" aria-label="Open as a full page">
              <OpenInFullIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          <IconButton onClick={close} size="small" aria-label="Close">
            <CloseIcon fontSize="small" />
          </IconButton>
        </Box>
      </DialogTitle>
      <DialogContent dividers>
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
      </DialogContent>
    </Dialog>
  )
}
