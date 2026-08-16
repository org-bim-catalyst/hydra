import {
  Background,
  Controls,
  MiniMap,
  Panel,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Connection,
  type Edge,
  type NodeTypes,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Button, Stack, Tooltip } from '@mui/material'
import RedoIcon from '@mui/icons-material/Redo'
import UndoIcon from '@mui/icons-material/Undo'
import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined'
import { useCallback, useRef } from 'react'
import { areSchemaTypesCompatible } from '../nodeCatalog'
import { useWorkflowCanvasStore, type WorkflowCanvasNodeData } from '../store/workflowCanvasStore'
import { WorkflowFlowNode } from './WorkflowFlowNode'

const nodeTypes: NodeTypes = { workflowNode: WorkflowFlowNode }

const DRAG_DATA_TYPE = 'application/x-ask-lucy-workflow-node-type'

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  return target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable
}

function CanvasInner() {
  const { screenToFlowPosition } = useReactFlow()
  const wrapperRef = useRef<HTMLDivElement>(null)
  const clipboardRef = useRef<WorkflowCanvasNodeData[]>([])

  const nodes = useWorkflowCanvasStore((s) => s.nodes)
  const edges = useWorkflowCanvasStore((s) => s.edges)
  const onNodesChange = useWorkflowCanvasStore((s) => s.onNodesChange)
  const onEdgesChange = useWorkflowCanvasStore((s) => s.onEdgesChange)
  const onConnect = useWorkflowCanvasStore((s) => s.onConnect)
  const onNodeDragStop = useWorkflowCanvasStore((s) => s.onNodeDragStop)
  const addNode = useWorkflowCanvasStore((s) => s.addNode)
  const removeNodes = useWorkflowCanvasStore((s) => s.removeNodes)
  const pasteNodes = useWorkflowCanvasStore((s) => s.pasteNodes)
  const setSelectedNodeId = useWorkflowCanvasStore((s) => s.setSelectedNodeId)
  const undo = useWorkflowCanvasStore((s) => s.undo)
  const redo = useWorkflowCanvasStore((s) => s.redo)
  const autoLayout = useWorkflowCanvasStore((s) => s.autoLayout)
  const canUndo = useWorkflowCanvasStore((s) => s.past.length > 0)
  const canRedo = useWorkflowCanvasStore((s) => s.future.length > 0)

  const isValidConnection = useCallback(
    (edgeOrConnection: Edge | Connection) => {
      const sourceNode = nodes.find((n) => n.id === edgeOrConnection.source)
      const targetNode = nodes.find((n) => n.id === edgeOrConnection.target)
      if (!sourceNode || !targetNode) return false
      if (sourceNode.id === targetNode.id) return false
      return areSchemaTypesCompatible(sourceNode.data.outputSchemaJson, targetNode.data.inputSchemaJson)
    },
    [nodes],
  )

  const handleDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault()
      const nodeType = event.dataTransfer.getData(DRAG_DATA_TYPE)
      if (!nodeType) return
      const position = screenToFlowPosition({ x: event.clientX, y: event.clientY })
      addNode(nodeType as WorkflowCanvasNodeData['nodeType'], position)
    },
    [addNode, screenToFlowPosition],
  )

  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent) => {
      if (isEditableTarget(event.target)) return

      const isMac = navigator.platform.toLowerCase().includes('mac')
      const mod = isMac ? event.metaKey : event.ctrlKey

      if ((event.key === 'Delete' || event.key === 'Backspace') && !mod) {
        const selectedIds = nodes.filter((n) => n.selected).map((n) => n.id)
        if (selectedIds.length > 0) {
          event.preventDefault()
          removeNodes(selectedIds)
        }
        return
      }

      if (mod && event.key.toLowerCase() === 'c') {
        const selected = nodes.filter((n) => n.selected).map((n) => n.data)
        if (selected.length > 0) {
          clipboardRef.current = selected
        }
        return
      }

      if (mod && event.key.toLowerCase() === 'v') {
        if (clipboardRef.current.length > 0) {
          event.preventDefault()
          pasteNodes(clipboardRef.current)
        }
        return
      }

      if (mod && !event.shiftKey && event.key.toLowerCase() === 'z') {
        event.preventDefault()
        undo()
        return
      }

      if (mod && (event.key.toLowerCase() === 'y' || (event.shiftKey && event.key.toLowerCase() === 'z'))) {
        event.preventDefault()
        redo()
      }
    },
    [nodes, pasteNodes, removeNodes, undo, redo],
  )

  return (
    <div
      ref={wrapperRef}
      role="application"
      aria-label="Workflow canvas"
      tabIndex={0}
      onKeyDown={handleKeyDown}
      onDrop={handleDrop}
      onDragOver={(e) => e.preventDefault()}
      style={{ width: '100%', height: '100%', outline: 'none' }}
    >
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeDragStop={onNodeDragStop}
        isValidConnection={isValidConnection}
        onSelectionChange={({ nodes: selectedNodes }) => setSelectedNodeId(selectedNodes[0]?.id ?? null)}
        deleteKeyCode={null}
        multiSelectionKeyCode="Shift"
        fitView
        minZoom={0.2}
        maxZoom={2}
      >
        <Background />
        <Controls />
        <MiniMap pannable zoomable />
        <Panel position="top-right">
          <Stack direction="row" spacing={1}>
            <Tooltip title="Undo (Ctrl+Z)">
              <span>
                <Button size="small" variant="outlined" startIcon={<UndoIcon />} disabled={!canUndo} onClick={undo}>
                  Undo
                </Button>
              </span>
            </Tooltip>
            <Tooltip title="Redo (Ctrl+Shift+Z)">
              <span>
                <Button size="small" variant="outlined" startIcon={<RedoIcon />} disabled={!canRedo} onClick={redo}>
                  Redo
                </Button>
              </span>
            </Tooltip>
            <Tooltip title="Auto-layout">
              <Button size="small" variant="outlined" startIcon={<AccountTreeOutlinedIcon />} onClick={autoLayout}>
                Auto-layout
              </Button>
            </Tooltip>
          </Stack>
        </Panel>
      </ReactFlow>
    </div>
  )
}

/** spec.md User Story 2 — canvas/palette/config-panel composition lives in `WorkflowDesignerPage`; this component owns only the `@xyflow/react` surface (pan/zoom/minimap/multi-select/copy-paste/delete/typed-connection validation/auto-layout). */
export function WorkflowCanvas() {
  return (
    <ReactFlowProvider>
      <CanvasInner />
    </ReactFlowProvider>
  )
}

export { DRAG_DATA_TYPE }
