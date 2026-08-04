import { KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core'
import type { KnowledgeBaseFolder } from '../api/knowledgeBaseFoldersApi'

/**
 * Mouse drag-and-drop for the folder tree (FR-014, research.md Decision 6). The keyboard-
 * accessible equivalent FR-040 requires is deliberately NOT a custom keyboard-drag
 * interaction on top of these same sensors — dnd-kit's `KeyboardSensor` is designed for
 * linear sortable lists, not an arbitrary tree, so wiring it here would be a fragile,
 * hard-to-test reach. Instead, every draggable item's context menu offers an explicit
 * "Move to…" action — precisely the equivalent FR-040 itself names as an acceptable pattern
 * ("e.g., a 'Move to folder' action") — reaching the identical `actions/move` endpoint a
 * mouse drop would. `KeyboardSensor` is still included below so a drag that WAS initiated
 * with a pointer can be completed/cancelled from the keyboard mid-gesture (Escape/Space),
 * per dnd-kit's own accessibility guidance.
 */
export function useKnowledgeBaseDndSensors() {
  return useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  )
}

export interface FolderTreeNode {
  folder: KnowledgeBaseFolder
  children: FolderTreeNode[]
}

/** Builds a nested tree from the flat folder list the API returns (data-model.md — folders carry `parentFolderId`, not a navigation collection). */
export function buildFolderTree(folders: KnowledgeBaseFolder[]): FolderTreeNode[] {
  const byId = new Map<string, FolderTreeNode>(folders.map((folder) => [folder.id, { folder, children: [] }]))
  const roots: FolderTreeNode[] = []

  for (const node of byId.values()) {
    if (node.folder.parentFolderId && byId.has(node.folder.parentFolderId)) {
      byId.get(node.folder.parentFolderId)!.children.push(node)
    } else {
      roots.push(node)
    }
  }

  return roots
}
