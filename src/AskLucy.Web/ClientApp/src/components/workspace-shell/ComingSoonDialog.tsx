import { Dialog, DialogContent, DialogTitle, IconButton, Typography } from '@mui/material'
import CloseIcon from '@mui/icons-material/Close'
import { useComingSoonStore } from '../../store/comingSoonStore'

/** One shared dialog for every not-yet-implemented tool action across the workspace
 * (FR-012/FR-021) — mount once (`ChatPage.tsx`), driven by `useComingSoonStore`. */
export function ComingSoonDialog() {
  const featureLabel = useComingSoonStore((s) => s.featureLabel)
  const hide = useComingSoonStore((s) => s.hide)

  return (
    <Dialog open={featureLabel !== null} onClose={hide} maxWidth="xs" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        {featureLabel}
        <IconButton onClick={hide} aria-label="Close" size="small">
          <CloseIcon fontSize="small" />
        </IconButton>
      </DialogTitle>
      <DialogContent>
        <Typography color="text.secondary">
          {featureLabel} is coming soon to the Studio workspace.
        </Typography>
      </DialogContent>
    </Dialog>
  )
}
