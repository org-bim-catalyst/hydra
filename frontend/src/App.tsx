import { CssBaseline, ThemeProvider } from '@mui/material'
import { useMemo } from 'react'
import { QueryProvider } from './app/QueryProvider'
import { AppRouter } from './routes/router'
import { useThemeStore } from './store/themeStore'
import { createAppTheme } from './theme'

function App() {
  const mode = useThemeStore((state) => state.mode)
  const theme = useMemo(() => createAppTheme(mode), [mode])

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <QueryProvider>
        <AppRouter />
      </QueryProvider>
    </ThemeProvider>
  )
}

export default App
