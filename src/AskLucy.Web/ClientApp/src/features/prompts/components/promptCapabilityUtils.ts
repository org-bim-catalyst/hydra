import type { ModelCapabilities, ModelSummary } from '../../chat/api/aiProvidersApi'
import type { PromptCapabilityRequirements } from '../api/promptsApi'

/** Which of a prompt's required capabilities a given model does not support (spec.md FR-004) — shared by the Testing Console and the chat composer's "Insert Prompt" picker. */
export function unmetCapabilities(required: PromptCapabilityRequirements, model: ModelSummary): string[] {
  const map: [boolean, keyof ModelCapabilities, string][] = [
    [required.requiresStreaming, 'streaming', 'streaming'],
    [required.requiresVision, 'vision', 'vision'],
    [required.requiresFunctionCalling, 'functionCalling', 'function calling'],
    [required.requiresJsonMode, 'jsonMode', 'JSON mode'],
    [required.requiresReasoning, 'reasoning', 'reasoning'],
    [required.requiresEmbeddings, 'embeddings', 'embeddings'],
    [required.requiresImageInput, 'imageInput', 'image input'],
    [required.requiresImageOutput, 'imageOutput', 'image output'],
    [required.requiresAudio, 'audio', 'audio'],
  ]
  return map.filter(([isRequired, key]) => isRequired && !model.capabilities[key]).map(([, , label]) => label)
}

export function isModelCompatible(required: PromptCapabilityRequirements, model: ModelSummary): boolean {
  return unmetCapabilities(required, model).length === 0
}
