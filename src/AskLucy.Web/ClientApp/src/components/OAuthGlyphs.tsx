/** Simple, single-color Google/Facebook glyphs for "Continue with…" OAuth buttons
 * (AuthLayout callers only) — `currentColor` so they always match the button's own text
 * color/contrast instead of clashing with it as the multi-color brand marks did. */
export function GoogleGlyph() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path
        fill="currentColor"
        d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1Z"
      />
      <path
        fill="currentColor"
        d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.99.66-2.25 1.06-3.71 1.06-2.86 0-5.29-1.93-6.15-4.53H2.18v2.85A11 11 0 0 0 12 23Z"
      />
      <path
        fill="currentColor"
        d="M5.85 14.1a6.6 6.6 0 0 1-.35-2.1c0-.73.13-1.44.35-2.1V7.05H2.18A11 11 0 0 0 1 12c0 1.78.43 3.46 1.18 4.95l3.67-2.85Z"
      />
      <path
        fill="currentColor"
        d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1a11 11 0 0 0-9.82 6.05l3.67 2.85C6.71 7.3 9.14 5.38 12 5.38Z"
      />
    </svg>
  )
}

export function FacebookGlyph() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path
        fill="currentColor"
        d="M14.5 22v-8.4h2.82l.42-3.27H14.5V8.24c0-.95.26-1.59 1.62-1.59h1.73V3.74A23 23 0 0 0 15.32 3.6c-2.5 0-4.21 1.53-4.21 4.33v2.4H8.28v3.27h2.83V22h3.39Z"
      />
    </svg>
  )
}
