import {
  RiArrowGoBackLine,
  RiArrowGoForwardLine,
  RiCheckLine,
  RiCloseLine,
  RiEdit2Line,
  RiRestartLine,
  RiRoundedCorner,
  } from '@remixicon/react'
import {
  ExpandableActionGroup,
  type ExpandableActionGroupAction,
} from '../../components/workspace-shell/ExpandableActionGroup'
import { useActiveSiteBoundaryStore } from '../../store/activeSiteBoundaryStore'
import { useOutlineResetStore } from '../../viewer/siteBoundaryEdit/outlineResetStore'
import { siteBoundaryEditActions } from '../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from '../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { AddCircleShapeIcon, AddCornerIcon, DeleteCornerIcon, RingToCircleIcon, CurveEdgeIcon, CutCircleShapeIcon, DrawArcIcon, SelectCornersIcon } from './outlineToolIcons'

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
  const selecting = session?.tool === 'select'
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
      id: 'select-corners',
      label: selecting ? 'Stop selecting corners' : 'Select corners',
      icon: <SelectCornersIcon size={20} />,
      onSelect: siteBoundaryEditActions.toggleSelectTool,
      highlighted: selecting,
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'add-corner',
      label: 'Add corner',
      icon: <AddCornerIcon size={20} />,
      onSelect: siteBoundaryEditActions.addCorner,
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select a corner first' : notEditing,
    },
    {
      id: 'delete-corner',
      label: session && session.selectedCorners.length > 1 ? `Delete ${session.selectedCorners.length} corners` : 'Delete corner',
      icon: <DeleteCornerIcon size={20} />,
      onSelect: siteBoundaryEditActions.deleteCorner,
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select a corner first' : notEditing,
    },
    {
      id: 'round-corner',
      label: 'Round corner',
      icon: <RiRoundedCorner size={20} />,
      onSelect: () => siteBoundaryEditActions.openShapeDialog('round'),
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select a corner first' : notEditing,
    },
    {
      id: 'curve-edge',
      label: 'Curve edge',
      icon: <CurveEdgeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.openShapeDialog('curve'),
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select the corner at the start of the edge first' : notEditing,
    },
    {
      id: 'draw-arc',
      label: 'Draw arc',
      icon: <DrawArcIcon size={20} />,
      onSelect: siteBoundaryEditActions.startArc,
      highlighted: session?.tool === 'arc',
      disabled: !editing || session.selectedCorners.length !== 2 || saving,
      disabledReason: editing ? 'Select exactly two corners first (Ctrl-click each)' : notEditing,
    },
    {
      id: 'add-circle',
      label: 'Add circle',
      icon: <AddCircleShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startCircle('add'),
      highlighted: session?.tool === 'circle' && session.circleOperation === 'add',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'cut-circle',
      label: 'Cut circle',
      icon: <CutCircleShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startCircle('cut'),
      highlighted: session?.tool === 'circle' && session.circleOperation === 'cut',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'make-circle',
      label: 'Make ring a circle',
      icon: <RingToCircleIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.openShapeDialog('circle'),
      disabled: !editing || saving,
      disabledReason: notEditing,
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
      // While editing, the open edit is given up first: a reset discards every hand edit anyway, and the
      // editor must not stay open on rings the reset is about to replace.
      onSelect: () => {
        if (editing) siteBoundaryEditActions.cancel()
        useOutlineResetStore.getState().show()
      },
      disabled: !isHandEdited || saving,
      disabledReason: saving
        ? 'Wait for the save to finish'
        : "This outline is the one Lucy found, so there's nothing to reset. Save an edit first.",
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
