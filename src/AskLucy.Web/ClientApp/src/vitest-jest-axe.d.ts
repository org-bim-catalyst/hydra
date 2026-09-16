// jest-axe's own types augment jest's `Matchers`/`@jest/expect`, not Vitest's — Vitest 5 dropped
// the compatibility bridge that used to pick those up automatically (Vitest 4 didn't need this
// file). Augmenting Vitest's own `Matchers<R, T>` merge point here fixes every `*.a11y.test.tsx`
// file at once rather than touching each one.
import 'vitest'

declare module 'vitest' {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- must match Matchers<R, T>'s arity to merge with it
  interface Matchers<R = void, T = unknown> {
    toHaveNoViolations(): R
  }
}
