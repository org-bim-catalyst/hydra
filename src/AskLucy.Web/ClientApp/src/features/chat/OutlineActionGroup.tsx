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
import {
  AddCircleShapeIcon,
  AddCornerIcon,
  AddPolygonShapeIcon,
  AddRectangleShapeIcon,
  AddSquareShapeIcon,
  CurveEdgeIcon,
  CutCircleShapeIcon,
  CutPolygonShapeIcon,
  CutRectangleShapeIcon,
  CutSquareShapeIcon,
  DeleteCornerIcon,
  DrawArcIcon,
  RingToCircleIcon,
  SelectCornersIcon,
} from './outlineToolIcons'
import {
  orderOf,
  useOutlineToolGroupStore,
  type OutlineToolGroupId,
} from './outlineToolGroupStore'

/**
 * specs/079-site-boundary-manual-editing: the outline editor's actions in one place. Each button is
 * live only when it can act - Edit needs an outline and no open session, Add/Delete need a selected
 * corner, Done needs a change - and a greyed-out one says why in its tooltip.
 *
 * specs/081 US6: the drawing tools sit in six groups (Photoshop-style). A group's button shows the tool
 * that is active, else the one last chosen from it; its menu lists the group's tools.
 */
export function OutlineActionGroup() {
  const session = useSiteBoundaryEditStore((s) => s.session)
  const dirty = useSiteBoundaryEditStore((s) => s.isDirty())
  const hasOutline = useActiveSiteBoundaryStore((s) => s.polygon !== null)
  const isHandEdited = useActiveSiteBoundaryStore((s) => s.isHandEdited)
  const order = useOutlineToolGroupStore((s) => s.order)

  const editing = session !== null
  const saving = session?.status.kind === 'saving'
  const hasCorner = session?.selectedCorner != null
  const selecting = session?.tool === 'select'
  const notEditing = 'Start editing the outline first'

  const tools: ExpandableActionGroupAction[] = [
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
      swapOnly: true,
      label: session && session.selectedCorners.length > 1 ? `Delete ${session.selectedCorners.length} corners` : 'Delete corner',
      icon: <DeleteCornerIcon size={20} />,
      onSelect: siteBoundaryEditActions.deleteCorner,
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select a corner first' : notEditing,
    },
    {
      id: 'round-corner',
      swapOnly: true,
      label: 'Round corner',
      icon: <RiRoundedCorner size={20} />,
      onSelect: () => siteBoundaryEditActions.openShapeDialog('round'),
      disabled: !editing || !hasCorner || saving,
      disabledReason: editing ? 'Select a corner first' : notEditing,
    },
    {
      id: 'curve-edge',
      swapOnly: true,
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
      highlighted: session?.tool === 'circle' && session.shapeKind === 'circle' && session.circleOperation === 'add',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'cut-circle',
      label: 'Cut circle',
      icon: <CutCircleShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startCircle('cut'),
      highlighted: session?.tool === 'circle' && session.shapeKind === 'circle' && session.circleOperation === 'cut',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'make-circle',
      swapOnly: true,
      label: 'Make ring a circle',
      icon: <RingToCircleIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.openShapeDialog('circle'),
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'add-rectangle',
      label: 'Add rectangle',
      icon: <AddRectangleShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startShape('rectangle', 'add'),
      highlighted: session?.tool === 'circle' && session.shapeKind === 'rectangle' && session.circleOperation === 'add',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'cut-rectangle',
      label: 'Cut rectangle',
      icon: <CutRectangleShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startShape('rectangle', 'cut'),
      highlighted: session?.tool === 'circle' && session.shapeKind === 'rectangle' && session.circleOperation === 'cut',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'add-square',
      label: 'Add square',
      icon: <AddSquareShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startShape('square', 'add'),
      highlighted: session?.tool === 'circle' && session.shapeKind === 'square' && session.circleOperation === 'add',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'cut-square',
      label: 'Cut square',
      icon: <CutSquareShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startShape('square', 'cut'),
      highlighted: session?.tool === 'circle' && session.shapeKind === 'square' && session.circleOperation === 'cut',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'add-polygon',
      label: 'Add polygon',
      icon: <AddPolygonShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startPolygon('add'),
      highlighted: session?.tool === 'polygon' && session.polygonOperation === 'add',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
    {
      id: 'cut-polygon',
      label: 'Cut polygon',
      icon: <CutPolygonShapeIcon size={20} />,
      onSelect: () => siteBoundaryEditActions.startPolygon('cut'),
      highlighted: session?.tool === 'polygon' && session.polygonOperation === 'cut',
      disabled: !editing || saving,
      disabledReason: notEditing,
    },
  ]
  const byId = new Map(tools.map((tool) => [tool.id, tool]))

  // One group button: shows the active tool in it, else the group's first tool. Its flyout holds the others, in order.
  // Choosing a flyout tool swaps it with the button's tool; a swap-only tool then waits for the button press to run.
  const group = (id: OutlineToolGroupId): ExpandableActionGroupAction => {
    const members = orderOf(id, order).map((toolId) => byId.get(toolId)!)
    const active = members.find((tool) => tool.highlighted)
    const shown = active ?? members[0]
    return {
      id: `group-${id}`,
      label: shown.label,
      icon: shown.icon,
      highlighted: Boolean(active),
      disabled: shown.disabled,
      disabledReason: shown.disabledReason,
      tools: members,
      activeToolId: shown.id,
      onChooseTool: (toolId) => {
        useOutlineToolGroupStore.getState().choose(id, toolId)
        const tool = byId.get(toolId)
        if (!tool?.swapOnly) tool?.onSelect?.()
      },
    }
  }

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
    group('edit-corners'),
    group('add-corner'),
    group('curve-edge'),
    group('circle'),
    group('rectangle'),
    group('polygon'),
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
