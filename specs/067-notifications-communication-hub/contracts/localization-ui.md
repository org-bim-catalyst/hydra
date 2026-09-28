# Contract: Frontend Localization & RTL

**Scope**: This contract covers the notification center (bell popover and full page), notification details, notification preferences and the language switch. It also covers the entire admin area (FR-046, FR-046a). Every other surface stays English, left-to-right, and on centralized copy (Assumptions: Arabic scope). Decision records are in [research.md](../research.md) R16–R17 and ADR 0019.

---

## Module layout (`ClientApp/src/i18n/`)

```text
i18n/
├── LocalizedSurface.tsx        # lang/dir + RTL theme + RTL emotion cache for a subtree
├── useLocalization.ts          # TanStack Query: GET/PUT /users/me/localization
├── useT.ts                     # useT(namespace) → t(key, params?)
├── format.ts                   # formatNumber/formatDate/formatRelative (Intl, Western digits, Gregorian)
├── protectedTerms.ts           # mirror of docs/localization/do-not-translate.md (R17)
├── types.ts                    # MessagesOf<T>, Namespace, Language
└── messages/
    ├── en/
    │   ├── common.ts           # shared buttons, states, toasts, validation (Zod) messages
    │   ├── notifications.ts    # center, details, preferences, language switch
    │   └── admin/
    │       ├── shell.ts        # page title/subtitle, sidebar, collapse control, header controls
    │       ├── dashboard.ts  users.ts  roles.ts  roleAssignments.ts  systemAgents.ts
    │       ├── aiProviders.ts  defaultModels.ts  aiCapabilities.ts  agentPolicies.ts
    │       ├── workflowPolicies.ts  mcpServers.ts  jobs.ts
    │       └── notifications.ts  # dashboard, deliveries, templates, announcements, localization
    └── ar/                      # same files; each `satisfies MessagesOf<typeof en…>`
```

## API

```ts
// Compile-time completeness: a missing or extra Arabic key fails `tsc -b` (SC-015).
export const arNotifications = { … } satisfies MessagesOf<typeof enNotifications>;

const t = useT('admin.notifications');
t('deliveries.retrySucceeded', { count: 3 });   // "{count}" interpolation; plural via Intl.PluralRules
```

- **Message values**: a message is either a string or a plural object `{ zero?, one, two?, few?, many?, other }`. Arabic needs `zero`, `one`, `two`, `few`, `many` and `other`, and a unit test asserts every Arabic plural object has all six.
- **Interpolated values**: `{name}` params are inserted as React text, never as HTML. User-entered content (role, agent or user names, template variables, announcement text) is **only ever passed as a param** and is never looked up as a key (FR-046b).
- **Fallback**: a missing key at runtime renders the English string and logs a console error in development. Production can't hit that path, because `tsc` enforces completeness.
- **Formatting**: `format.ts` uses:
  - `ar-u-nu-latn` for numbers, which gives Western digits (Assumptions);
  - `ar-u-ca-gregory-nu-latn` for dates, giving the Gregorian calendar with Arabic month names;
  - relative times through `Intl.RelativeTimeFormat`.
- **Server text**: server-originated `title`, `detail` and `errors` are displayed as returned. The server has already localized them for localized surfaces (R15). `traceId` and codes are shown verbatim.

## `LocalizedSurface`

```tsx
<LocalizedSurface scope="page">  {/* admin routes: sets <html lang dir> while mounted, restores on unmount */}
<LocalizedSurface scope="subtree"> {/* bell popover, notification pages: wraps its own content in <div lang dir> */}
```

`LocalizedSurface` provides:
- the effective `lang` and `dir` from `useLocalization()`;
- `ThemeProvider` with `createTheme({ ...baseTheme, direction })`, preserving light and dark mode from `themeStore`;
- `CacheProvider` with a `createCache({ key: 'muirtl', stylisPlugins: [prefixer, rtlPlugin] })` cache when the direction is `rtl`, and the default cache otherwise;
- default `slotProps` so portaled MUI surfaces (Popover, Menu, Dialog, Tooltip, Snackbar) inside the subtree get the same `dir`.

When localization is disabled, or the effective language is `en`, the component is a pass-through. No extra providers are mounted, so English rendering is byte-identical to today.

## RTL rules for existing and new admin screens

| Element | Rule |
|---|---|
| Layout | Use logical props only: MUI `sx` with `marginInlineStart`, `paddingInlineEnd`, `insetInlineStart`. Physical `left`/`right` in admin code is flagged in review. |
| Directional icons | Back, forward, chevrons and the sidebar collapse arrow flip by `direction`. Non-directional icons (search, settings, brand logos) never flip. |
| d3 charts | The SVG root keeps `direction="ltr"`, so time axes run left to right (Assumptions). Legends, captions and axis *labels* are translated. Tooltips follow the page direction. |
| Tables | Column order mirrors. Numeric and identifier cells use `dir="ltr"` with `<bdi>` so mixed-direction values render correctly (FR-045). |
| Protected terms in Arabic text | Wrap them in `<bdi>` when the term is interpolated, so Latin-script terms don't reorder surrounding punctuation. |
| Truncation | Long labels use `noWrap` plus a tooltip, with the full text in `aria-label` (spec edge case). They must not overlap other content. |
| Jobs | The sidebar label and helper text are translated. The external Hangfire dashboard is not (Assumptions). |

## Language switch

- **Placement**: a `LanguageSwitch` appears in the account menu and on the notification preferences page. It is shown **only** when `localizationEnabled` is true and more than one language is supported (FR-044b).
- **Changing language**: it calls `PUT /users/me/localization`, which invalidates `['localization']`. Every `LocalizedSurface` re-renders in the new direction without a reload.
- **Errors**: a failure shows a toast with a retry, following the frontend error rule in CLAUDE.md.

## Tests (frontend)

| Test | Asserts |
|---|---|
| `i18n/catalogCompleteness.test.ts` | Every Arabic plural object has all six forms. No `en` value is identical in `ar`, except allow-listed protected-term-only strings. |
| `i18n/protectedTerms.test.ts` | For every English string containing a protected term, its Arabic counterpart contains the identical term (SC-016). |
| Page tests per admin section and each notification screen | Rendered once in `ar` + `rtl`. They assert `dir="rtl"` on the surface root, no untranslated catalog fallbacks (dev fallback spy), and **jest-axe** has no serious or critical violations in light, dark and RTL (SC-011). |
| `LocalizedSurface.test.tsx` | A pass-through when disabled. Portaled Dialog and Menu content carries `dir="rtl"`. |

Known jsdom traps apply: open MUI dialogs throw from `getByRole` (use `getByText`), and Pointer Capture is missing.
