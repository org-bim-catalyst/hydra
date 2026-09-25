import { RiHomeLine } from '@remixicon/react'
import { Fab, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import { CIRCULAR_BUTTON_SX } from '../../../components/workspace-shell/circularActionChrome'
import { HudCard } from '../../../components/workspace-shell/HudCard'
import { VIEW_LANDING_STATE } from '../../../routes/viewLandingState'

/** The readdy.ai reference's top-left "Home › destination" pair. There is no "project" or
 * "location" entity yet, so the title is the workspace's own name (FR-001's "Flumeria Studio").
 *
 * - **Home** is a circular button in the same chrome as the other floating controls
 *   (`ThemeToggleButton`). It opens the landing page. Because `/` redirects a signed-in visitor
 *   back to `/studio` (`PublicOnlyRoute`), it passes {@link VIEW_LANDING_STATE} to say the
 *   visit is deliberate.
 * - **The title** is plain text on the shared `HudCard` surface, like the weather label and site
 *   card beside it. It is a label, not a link.
 *
 * specs/073: returns the two as siblings, not a positioned pair — they are the first two items of
 * the studio's top-left HUD row (`WorkspaceOverlay`'s `topStart`), which owns the positioning. */
export function HomeProjectCard() {
  const navigate = useNavigate()

  return (
    <>
      <Fab
        size="small"
        aria-label="Home"
        onClick={() => navigate('/', { state: VIEW_LANDING_STATE })}
        sx={{ ...CIRCULAR_BUTTON_SX, pointerEvents: 'auto' }}
      >
        <RiHomeLine size={20} />
      </Fab>
      <HudCard>
        <Typography variant="subtitle2" component="span" noWrap sx={{ fontWeight: 600 }}>
          Flumeria Studio
        </Typography>
      </HudCard>
    </>
  )
}
