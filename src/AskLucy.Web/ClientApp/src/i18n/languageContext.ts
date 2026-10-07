import { createContext } from 'react'
import { DEFAULT_LANGUAGE, type Direction, type Language } from './types'

export interface LanguageContextValue {
  language: Language
  direction: Direction
  /** True inside a mounted `LocalizedSurface`; a nested subtree surface for the same language then adds nothing. */
  active: boolean
}

/** English, left-to-right, unless a `LocalizedSurface` above says otherwise: no provider means no change. */
export const LanguageContext = createContext<LanguageContextValue>({
  language: DEFAULT_LANGUAGE,
  direction: 'ltr',
  active: false,
})
