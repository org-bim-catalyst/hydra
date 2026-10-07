import { arCommon } from './ar/common'
import { arNotifications } from './ar/notifications'
import { enCommon } from './en/common'
import { enNotifications } from './en/notifications'

/** Every catalog, by language and namespace. A namespace added here is available to `useT` at once. */
export const catalogs = {
  en: { common: enCommon, notifications: enNotifications },
  ar: { common: arCommon, notifications: arNotifications },
} as const

export type Namespace = keyof (typeof catalogs)['en']
export type EnglishCatalogs = (typeof catalogs)['en']
