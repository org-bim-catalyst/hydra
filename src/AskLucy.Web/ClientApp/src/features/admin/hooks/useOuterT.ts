import { useMemo } from 'react'
import { useEffectiveLocalization } from '../../../i18n/useLocalization'
import type { Namespace } from '../../../i18n/messages'
import { createT, type Translate } from '../../../i18n/useT'

/**
 * The translate function for text a page hands to `AdminShell` as props (its title, subtitle and actions). The page
 * itself renders outside the shell's language surface, so `useT` would read English there; this reads the caller's
 * effective language directly. Anything rendered inside the shell uses `useT` as usual.
 */
export function useOuterT<N extends Namespace>(namespace: N): Translate<N> {
  const { language } = useEffectiveLocalization()
  return useMemo(() => createT(namespace, language), [namespace, language])
}
