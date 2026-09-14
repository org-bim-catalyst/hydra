import { createContext, useContext } from 'react'
import type { PanelDensity } from './chrome'

/** Carries a panel's declared density (`PanelChrome.density`) down to whatever it renders — content
 * blocks and live panels alike — so each can lay itself out compactly without a prop threaded
 * through every level. */
export const PanelDensityContext = createContext<PanelDensity>('comfortable')

export function usePanelDensity(): PanelDensity {
  return useContext(PanelDensityContext)
}
