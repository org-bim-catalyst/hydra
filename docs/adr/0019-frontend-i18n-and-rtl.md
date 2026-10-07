# ADR 0019: Frontend Internationalization and Right-to-Left Support

**Status:** Accepted

**Date:** 2026-10-07

**Feature:** [specs/067-notifications-communication-hub](../../specs/067-notifications-communication-hub/spec.md)

> Numbering note: `docs/adr/0019-dictation-engine-policy.md` (specs/078) was written
> concurrently and also carries number 0019. The plan and tasks for spec 067 refer to this
> decision as "ADR 0019", so the number is kept here. Renumber one of the two when the
> branches merge, and update the references in specs/067.

## Context

Spec 067 delivers full Arabic for the notification centre, notification preferences and the
entire admin area. English stays the main language of the product, and the rest of the app
(chat, workspace, studio, landing, auth) stays English. Arabic is shown only while an
administrator has enabled localization and the user has chosen it.

The frontend has no i18n runtime today. Copy is centralized per surface but written as English
literals. This feature introduces the first locale beyond the platform default, so constitution
§7 (Internationalization) now applies: once an i18n framework is introduced, user-facing strings
must not be hardcoded inline. Arabic also needs right-to-left layout, which MUI and Emotion do not
provide on their own.

## Decision

### 1. An in-house, typed `i18n/` module instead of a library

`ClientApp/src/i18n/` provides typed catalogs per surface, a `useT(namespace)` hook,
`Intl`-based formatting, the protected-term helper and a TanStack Query hook for the effective
language.

- Arabic catalogs are typed `satisfies MessagesOf<typeof en>`, so a missing Arabic key fails
  `tsc -b`. The "every string is Arabic" criterion (SC-015) is enforced at compile time.
- `useT` interpolates `{name}` and resolves plurals with `Intl.PluralRules` (Arabic has six
  plural forms).
- Numbers and dates use `Intl.NumberFormat('ar-u-nu-latn')` and
  `Intl.DateTimeFormat('ar-u-ca-gregory-nu-latn')`: Western digits, Gregorian calendar, Arabic
  month names.
- The effective language is server state (`GET /api/v1/users/me/localization`) held in TanStack
  Query and invalidated on change. It is not duplicated in Zustand (§7).
- Brand names and acronyms never go through the catalogs. They come from
  `docs/localization/do-not-translate.md`, mirrored by `i18n/protectedTerms.ts` and, on the
  backend, `ProtectedTerms.cs`.

Rejected alternatives:

- `react-i18next` / `i18next`: mature, but two runtime dependencies and string keys with no
  compile-time completeness. Deferred to whenever the whole app is localized.
- FormatJS (`react-intl`): a heavier ICU runtime that still needs its own RTL handling.

### 2. Scope: notification screens and the admin area only

Only the notification screens (bell popover, centre, preferences) and `/admin/*` move onto the
catalogs. This is the justified exception recorded under Complexity Tracking in
[plan.md](../../specs/067-notifications-communication-hub/plan.md).

Constitution §7 says strings must not be hardcoded inline once an i18n framework exists. Moving
the whole app at once would multiply the scope and risk of this feature for no user-visible
benefit, because the spec keeps those screens English. The mitigation is the constitution's own
pre-i18n rule: every other surface already keeps its copy centralized, so extraction is
mechanical. Each further surface moves onto `i18n/` catalogs when it is localized, and that move
needs no new decision. If the whole app is ever localized, revisit the library choice above.

### 3. Right-to-left through an RTL Emotion cache

`LocalizedSurface` wraps a localized subtree and provides `lang`, `dir`, an MUI theme with
`direction: 'rtl'` that keeps the light/dark choice from the existing theme store, and an
Emotion `CacheProvider` using `stylis-plugin-rtl` (a `muirtl` cache, with portal slot props so
menus, dialogs and popovers inherit direction).

- On `/admin/*` the page shell sets `<html lang dir>` for the whole page, including the header
  controls. The bell popover and notification pages wrap only their own content, so the rest of
  the app is untouched.
- `stylis-plugin-rtl` is MUI's documented mechanism. Hand-flipping styles across the twelve
  existing admin sections is not viable.
- New dependencies: `stylis-plugin-rtl`, and an explicit `@emotion/cache` (already present
  transitively).

Rejected: global RTL for the whole app. It contradicts the Arabic scope.

### 4. d3 charts stay left-to-right

Time series read left to right in every locale, so d3 charts render their plot and axes inside
`dir="ltr"` groups. Their captions, titles and legends are translated and laid out right-to-left
around the chart. Mirroring a time axis would put the past on the right and mislead readers
used to Western-digit dates.

## Consequences

- No new runtime i18n dependency, and missing Arabic keys cannot ship. The cost is that we own
  plural, interpolation and formatting code (kept thin by leaning on `Intl`) and must revisit the
  decision if more locales or ICU message features arrive.
- Two dependencies are added: `stylis-plugin-rtl` and `@emotion/cache`.
- Surfaces outside the scope stay English with centralized copy, which is a deliberate and
  documented deviation from the letter of §7.
- Components in localized subtrees must use logical CSS properties or rely on the RTL cache.
  Axe checks run per screen in light, dark and RTL.
- Emails follow the same rules server-side: `lang`/`dir` on the root, Western digits, Gregorian
  dates, and protected terms wrapped in `<bdi>`.
