import { AiActivityIndicator } from '../../../components/AiActivityIndicator'

/**
 * Three-dot "thinking" indicator shown in place of the assistant's reply bubble while a
 * send is in flight and no content has streamed in yet (FR-006/FR-007). Renders the
 * shared `AiActivityIndicator` (SPEC-017 research.md #5) — which, unlike this component's
 * prior standalone implementation, respects `prefers-reduced-motion` (SPEC-017 FR-010),
 * superseding the earlier "always animates" decision.
 */
export function ThinkingIndicator({ label }: { label?: string }) {
  // `label` names the specific work when the server told us what it is — "Finding the site
  // boundary" — so a long wait says what it is waiting for instead of just "thinking".
  return <AiActivityIndicator state="thinking" label={label ?? 'Ask Lucy is thinking'} />
}
