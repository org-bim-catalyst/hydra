import { Divider } from '@mui/material'
import { usePanelDensity } from '../../chrome/density'

/** contracts/content-vocabulary.md "divider" block — a structural separator. No fields. In a
 * compact panel the rows around it already carry hairline dividers, so it keeps its separator
 * semantics but shows as spacing rather than a second line. */
export function DividerBlockRenderer() {
  const density = usePanelDensity()
  return density === 'compact' ? <Divider sx={{ my: 0.75, borderColor: 'transparent' }} /> : <Divider sx={{ my: 0.5 }} />
}
