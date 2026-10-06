export type ApprovalLinkOutcome = 'pending' | 'decided' | 'notFound'

/**
 * specs/067 US5 — what an approval notification's `?approval={id}` deep link should do on an execution page:
 * open the pending approval, say it has already been decided, or say it isn't there. Null when the page was
 * opened without the parameter.
 */
export function resolveApprovalLink<T extends { id: string }>(
  approvals: readonly T[],
  approvalId: string | null,
  isPending: (approval: T) => boolean,
): { outcome: ApprovalLinkOutcome; approval: T | undefined } | null {
  if (!approvalId) {
    return null
  }

  const approval = approvals.find((a) => a.id === approvalId)
  if (!approval) {
    return { outcome: 'notFound', approval: undefined }
  }

  return { outcome: isPending(approval) ? 'pending' : 'decided', approval }
}
