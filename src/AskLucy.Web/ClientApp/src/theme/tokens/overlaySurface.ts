/**
 * Metrics for the account menu and the account modals, read off the readdy.ai reference's own
 * compiled CSS rather than estimated from a screenshot.
 *
 * Kept separate from `radius`/`shadows` because these are not our scale: they are the
 * reference's Tailwind values, and naming them here is what stops the next edit quietly
 * rounding 12 up to the nearest thing we already had (`radius.lg`, 14 — which is exactly how
 * the corners came out wrong the first time).
 *
 * Colours are deliberately absent. The reference is a single light-mode page with a fixed green
 * brand; this app has a light and a dark theme of its own, so every surface below maps its
 * `background-50` / `foreground-500` / `primary-500` through the MUI palette instead. Copying
 * its literal oklch values would break dark mode outright.
 */
export const overlaySurface = {
  /** Tailwind `rounded-xl` — the menu card and the modal panel. */
  panelRadius: 12,
  /** Tailwind `rounded-lg` — an individual menu row. */
  itemRadius: 8,
  /** Tailwind `rounded-md` — the modal's close button. */
  controlRadius: 6,

  /** Tailwind `shadow-lg`, on the dropdown. */
  menuShadow: '0 10px 15px -3px rgb(0 0 0 / 0.1), 0 4px 6px -4px rgb(0 0 0 / 0.1)',
  /** Tailwind `shadow-2xl`, on the modal panel. */
  modalShadow: '0 25px 50px -12px rgb(0 0 0 / 0.25)',

  /** `w-64` on the dropdown, and its `mt-2.5` offset from the trigger. */
  menuWidth: 256,
  menuOffset: 10,

  /**
   * `max-w-3xl` / `max-w-6xl`. The reference exposes exactly these two sizes; forms take the
   * narrow one and data-dense pages the wide one.
   */
  modalWidth: { default: 768, large: 1152 },

  /** The overlay's `py-6 md:py-10 px-4` — this is what makes the modal top-aligned, not centred. */
  overlayPaddingY: { xs: 3, md: 5 },
  overlayPaddingX: 2,

  /** `backdrop-blur-sm`. */
  backdropBlur: 'blur(4px)',

  /**
   * `animate-scale-in` and `animate-fade-in`, verbatim:
   *   @keyframes scaleIn { 0% { opacity:0; transform:scale(.96) } to { opacity:1; transform:scale(1) } }
   *   @keyframes fadeIn  { 0% { opacity:0 } to { opacity:1 } }
   *   .animate-scale-in { animation: .3s ease-out forwards scaleIn }
   */
  enterDurationMs: 300,
  enterEasing: 'cubic-bezier(0, 0, 0.2, 1)',
  enterScale: 0.96,
} as const
