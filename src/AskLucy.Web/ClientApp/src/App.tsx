import { CssBaseline, ThemeProvider } from '@mui/material'
import { useMemo } from 'react'
import { QueryProvider } from './app/QueryProvider'
import { LogoutErrorToast } from './components/LogoutErrorToast'
import { usePrefersReducedMotion } from './hooks/usePrefersReducedMotion'
import { AppRouter } from './routes/router'
import { useThemeStore } from './store/themeStore'
import { createAppTheme } from './theme'

function App() {
  const mode = useThemeStore((state) => state.mode)
  const prefersReducedMotion = usePrefersReducedMotion()
  const theme = useMemo(() => createAppTheme(mode, prefersReducedMotion), [mode, prefersReducedMotion])

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <QueryProvider>
        <AppRouter />
        {/* Outside the router: a failed sign-out still navigates away, so this must outlive every
            page shell that could otherwise have shown it. */}
        <LogoutErrorToast />
      </QueryProvider>
    </ThemeProvider>
  )
}

export default App
