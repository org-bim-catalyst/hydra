import { catalogs, type Namespace } from './messages'
import type { Message, MessageTree, PluralMessage } from './types'

export interface Leaf {
  path: string
  value: Message
}

export const isPluralMessage = (value: Message | MessageTree): value is PluralMessage =>
  typeof value === 'object' &&
  typeof (value as PluralMessage).other === 'string' &&
  typeof (value as PluralMessage).one === 'string'

/** Every message in a catalog tree, with its dotted path. */
export function leaves(tree: MessageTree, prefix = ''): Leaf[] {
  return Object.entries(tree).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key
    return typeof value === 'string' || isPluralMessage(value)
      ? [{ path, value }]
      : leaves(value, path)
  })
}

/** Each text a message can produce: the string itself, or every form of a plural. */
export const textsOf = (value: Message): string[] =>
  typeof value === 'string' ? [value] : Object.values(value)

export const namespaces = Object.keys(catalogs.en) as Namespace[]

export const catalogOf = (language: 'en' | 'ar', namespace: Namespace) =>
  catalogs[language][namespace] as MessageTree
