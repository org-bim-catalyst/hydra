import type { PromptVariable } from '../api/promptsApi'

/** Merges freshly-detected placeholder names into the existing variable list, preserving metadata already entered and dropping definitions for placeholders no longer referenced — called on every content-field change so the editor always mirrors FR-014's undeclared/unreferenced check. */
export function syncVariablesWithPlaceholders(existing: PromptVariable[], detectedNames: string[]): PromptVariable[] {
  const byName = new Map(existing.map((v) => [v.name, v]))
  return detectedNames.map(
    (name, index) =>
      byName.get(name) ?? {
        name,
        description: null,
        type: 'String',
        isRequired: true,
        defaultValue: null,
        exampleValue: null,
        validationRulesJson: null,
        orderIndex: index,
      },
  )
}
