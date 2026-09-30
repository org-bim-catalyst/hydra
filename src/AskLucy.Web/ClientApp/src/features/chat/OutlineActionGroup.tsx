import {
  RiAddCircleLine,
  RiArrowGoBackLine,
  RiArrowGoForwardLine,
  RiCheckLine,
  RiCloseLine,
  RiDeleteBinLine,
  RiEdit2Line,
  RiRestartLine,
} from '@remixicon/react'
import {
  ExpandableActionGroup,
  type ExpandableActionGroupAction,
} from '../../components/workspace-shell/ExpandableActionGroup'
import { useActiveSiteBoundaryStore } from '../../store/activeSiteBoundaryStore'
import { useComingSoonStore } from '../../store/comingSoonStore'
import { siteBoundaryEditActions } from '../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from '../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

/**
 * specs/079-site-boundary-manual-editing: the outline editor's actions in one place. Each button is
 * live only when it can act - Edit needs an outline and no open session, Add/Delete need a selected
 * corner, Done needs a change - and a greyed-out one says why in its tooltip.
 */
export function OutlineActionGroup() {
  const session = useSiteBoundaryEditStore((s) => s.session)
  const dirty = useSiteBoundaryEditStore((s) => s.isDirty())
  const hasOutline = useActiveSiteBoundaryStore((s) => s.polygon !== null)
  const isHandEdited = useActiveSiteBoundaryStore((s) => s.isHandEdited)

  const editing = session !== null
  const saving = session?.status.kind === 'saving'
  const hasCorner = session?.selectedCorner != null
  const notEditing = 'Start editing the outline first'

  const actions: ExpandableActionGroupAction[] = [
    {
      id: 'edit-outline',
      label: editing && session.toolbarHidden ? 'Show edit bar' : 'Edit outline',
      icon: <RiEdit2Line size={20} />,
      // While editing with the floating bar dismissed, this brings the bar back.
      onSelect: () => (editing ? useSiteBoundaryEditStore.getState().setToolbarHidden(false) : void siteBoundaryEditActions.start()),
      disabled: editing ? !session.toolbarHidden : !hasOutline,
      disabledReason: editing ? 'The edit bar is already showing' : 'Ask Lucy to outline a site first',
    },
    {
      id: 'add-corner',
      label: 'Add corner',
      icon: <RiAddCircleLine size={20} />,
      onSelect: siteBoundaryEditActions.addCorner,
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select a corner first' : notEditing,
    },
    {
      id: 'delete-corner',
      label: 'Delete corner',
      icon: <RiDeleteBinLine size={20} />,
      onSelect: siteBoundaryEditActions.deleteCorner,
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select a corner first' : notEditing,
    },
    {
      id: 'undo',
      label: 'Undo',
      icon: <RiArrowGoBackLine size={20} />,
      onSelect: siteBoundaryEditActions.undo,
      disabled: !editing || session.undo.length === 0 || saving,
      disabledReason: editing ? 'Nothing to undo' : notEditing,
    },
    {
      id: 'redo',
      label: 'Redo',
      icon: <RiArrowGoForwardLine size={20} />,
      onSelect: siteBoundaryEditActions.redo,
      disabled: !editing || session.redo.length === 0 || saving,
      disabledReason: editing ? 'Nothing to redo' : notEditing,
    },
    {
      id: 'cancel',
      label: 'Cancel',
      icon: <RiCloseLine size={20} />,
      onSelect: siteBoundaryEditActions.cancel,
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'reset',
      label: "Reset to Lucy's outline",
      icon: <RiRestartLine size={20} />,
      // Reset arrives with a later part of this feature (tasks.md Phase 7).
      onSelect: () => useComingSoonStore.getState().show('Outline reset'),
      disabled: !isHandEdited || editing,
      disabledReason: editing ? 'Finish editing first' : "This outline hasn't been edited",
    },
    {
      id: 'done',
      label: 'Done',
      icon: <RiCheckLine size={20} />,
      onSelect: () => void siteBoundaryEditActions.done(),
      disabled: !editing || !dirty || saving,
      disabledReason: editing ? 'Make a change first' : notEditing,
      highlighted: true,
    },
  ]

  return <ExpandableActionGroup actions={actions} />
}
