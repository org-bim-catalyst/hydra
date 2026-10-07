import createCache from '@emotion/cache'
import { CacheProvider } from '@emotion/react'
import { ThemeProvider, createTheme } from '@mui/material/styles'
import { useContext, useEffect, useMemo, type ReactNode } from 'react'
import { prefixer } from 'stylis'
import rtlPlugin from 'stylis-plugin-rtl'
import { usePrefersReducedMotion } from '../hooks/usePrefersReducedMotion'
import { useThemeStore } from '../store/themeStore'
import { createAppTheme } from '../theme'
import { LanguageContext } from './languageContext'
import { useEffectiveLocalization } from './useLocalization'
import type { Direction, Language } from './types'

export interface LocalizedSurfaceProps {
  /**
   * `page` sets `<html lang dir>` while mounted and restores it on unmount (admin routes). `subtree` wraps its own
   * content in `<div lang dir>` (the bell popover and the notification screens).
   */
  scope: 'page' | 'subtree'
  children: ReactNode
}

let rtlCache: ReturnType<typeof createCache> | undefined
/** One shared cache: MUI's documented RTL mechanism, flipping every style the subtree emits. */
const getRtlCache = () =>
  (rtlCache ??= createCache({ key: 'muirtl', stylisPlugins: [prefixer, rtlPlugin] }))

/**
 * Portaled MUI surfaces render outside the subtree's DOM, so they do not inherit its `dir`. Giving each a `dir`
 * through the theme's default props puts it on their root, which every descendant then inherits.
 */
function directionalComponents(direction: Direction) {
  return {
    MuiPopover: { defaultProps: { dir: direction } },
    MuiMenu: { defaultProps: { dir: direction } },
    MuiDialog: { defaultProps: { dir: direction } },
    MuiDrawer: { defaultProps: { dir: direction } },
    MuiSnackbar: { defaultProps: { dir: direction } },
    MuiTooltip: { defaultProps: { slotProps: { popper: { dir: direction } } } },
  }
}

function useDocumentLanguage(active: boolean, language: Language, direction: Direction) {
  useEffect(() => {
    if (!active) return undefined
    const root = document.documentElement
    const previous = { lang: root.getAttribute('lang'), dir: root.getAttribute('dir') }
    root.setAttribute('lang', language)
    root.setAttribute('dir', direction)
    return () => {
      for (const [name, value] of Object.entries(previous)) {
        if (value === null) root.removeAttribute(name)
        else root.setAttribute(name, value)
      }
    }
  }, [active, language, direction])
}

interface ActiveSurfaceProps extends LocalizedSurfaceProps {
  language: Language
  direction: Direction
}

function ActiveSurface({ scope, language, direction, children }: ActiveSurfaceProps) {
  const mode = useThemeStore((state) => state.mode)
  const prefersReducedMotion = usePrefersReducedMotion()
  // The app's own theme (light or dark from `themeStore`), re-created with the direction and the portal defaults.
  const theme = useMemo(
    () =>
      createTheme(createAppTheme(mode, prefersReducedMotion), {
        direction,
        components: directionalComponents(direction),
      }),
    [mode, prefersReducedMotion, direction],
  )
  const context = useMemo(() => ({ language, direction, active: true }), [language, direction])
  useDocumentLanguage(scope === 'page', language, direction)

  const themed = (
    <ThemeProvider theme={theme}>
      <LanguageContext.Provider value={context}>
        {scope === 'subtree' ? (
          // `display: contents` keeps this wrapper out of the surrounding layout; `dir` still inherits through it.
          <div lang={language} dir={direction} style={{ display: 'contents' }}>
            {children}
          </div>
        ) : (
          children
        )}
      </LanguageContext.Provider>
    </ThemeProvider>
  )

  return direction === 'rtl' ? (
    <CacheProvider value={getRtlCache()}>{themed}</CacheProvider>
  ) : (
    themed
  )
}

/**
 * Renders a subtree in the caller's language and direction (R16): `lang` and `dir`, an RTL theme that keeps the
 * `themeStore` light or dark mode, the `muirtl` Emotion cache, and `dir` on portaled surfaces.
 *
 * When localization is disabled, or the effective language is English, this is a pass-through: no provider is
 * mounted, so English rendering is exactly what it was before this module existed.
 */
export function LocalizedSurface({ scope, children }: LocalizedSurfaceProps) {
  const { language, direction, isLocalized } = useEffectiveLocalization()
  const outer = useContext(LanguageContext)
  // A surface nested in one for the same language (the bell wraps its popover; an admin page sits inside the route's page
  // surface and its shell asks for another) adds nothing: the outer one already set the language, direction and theme.
  if (!isLocalized || (outer.active && outer.language === language)) return <>{children}</>
  return (
    <ActiveSurface scope={scope} language={language} direction={direction}>
      {children}
    </ActiveSurface>
  )
}
