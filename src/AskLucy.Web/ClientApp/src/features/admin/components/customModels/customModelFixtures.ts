import type { CustomModelSummary, DeploymentState } from '../../api/adminCustomModelsApi'

/** Test fixture shared by the Custom Models component tests. */
export function customModel(overrides: Partial<CustomModelSummary> = {}): CustomModelSummary {
  return {
    id: 'model-1',
    name: 'supertonic-3',
    repositoryId: 'Supertone/supertonic-3',
    revision: 'main',
    resolvedCommitSha: null,
    sourceUrl: 'https://huggingface.co/Supertone/supertonic-3',
    destination: 'Models/supertonic-3',
    deploymentState: 'Completed',
    availability: 'Available',
    canMakeAvailable: true,
    availabilityBlockedReason: null,
    canRemove: false,
    canCancel: false,
    totalBytes: 1536 * 2 ** 20,
    transferredBytes: 1536 * 2 ** 20,
    totalFileCount: 12,
    completedFileCount: 12,
    currentFilePath: null,
    currentFileBytes: null,
    currentFileTotalBytes: null,
    overwrittenFileCount: 0,
    failureKind: null,
    failureReason: null,
    submittedBy: { id: 'admin-1', displayName: 'Admin' },
    createdAtUtc: '2026-09-23T10:00:00Z',
    startedAtUtc: '2026-09-23T10:00:05Z',
    finishedAtUtc: '2026-09-23T10:04:00Z',
    backsEngine: null,
    ...overrides,
  }
}

export const ALL_DEPLOYMENT_STATES: DeploymentState[] = [
  'Queued',
  'Listing',
  'Transferring',
  'Completed',
  'Failed',
  'Cancelled',
]

/** One row per deployment state, with the fields each state would really carry. */
export const modelsInEveryState: CustomModelSummary[] = ALL_DEPLOYMENT_STATES.map((state, index) =>
  customModel({
    id: `model-${index}`,
    name: `model-${state.toLowerCase()}`,
    deploymentState: state,
    totalBytes: state === 'Queued' || state === 'Listing' ? null : 2048,
    availability: 'Unavailable',
    canMakeAvailable: state === 'Completed',
    availabilityBlockedReason: state === 'Completed' ? null : 'The deployment has not completed.',
    canRemove: state === 'Failed' || state === 'Cancelled',
    failureKind: state === 'Failed' ? 'TargetConnectionLost' : null,
    failureReason: state === 'Failed' ? 'The connection to the deployment target was lost.' : null,
    canCancel: state === 'Queued' || state === 'Listing' || state === 'Transferring',
  }),
)
