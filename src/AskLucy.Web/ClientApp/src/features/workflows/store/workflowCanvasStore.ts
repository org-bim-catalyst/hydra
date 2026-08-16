import { applyEdgeChanges, applyNodeChanges, type Connection, type Edge, type EdgeChange, type Node, type NodeChange, type XYPosition } from '@xyflow/react'
import { create } from 'zustand'
import type { WorkflowNodeApprovalPolicy, WorkflowNodeType, WorkflowVariableKind, WorkflowVariableType } from '../api/workflowsApi'
import { getNodeCatalogEntry } from '../nodeCatalog'
import type { WorkflowDraftDefinition, WorkflowDraftVariableDefinition } from '../workflowDraftDefinition'
import { EMPTY_DRAFT_DEFINITION } from '../workflowDraftDefinition'

export type WorkflowCanvasNodeData = {
  nodeKey: string
  nodeType: WorkflowNodeType
  name: string
  description: string | null
  inputSchemaJson: string
  outputSchemaJson: string
  configurationJson: string
  requiredPermissionsJson: string
  timeoutSeconds: number | null
  retryPolicyJson: string | null
  approvalPolicy: WorkflowNodeApprovalPolicy
  idempotencyKeyExpression: string | null
  compensatingNodeKey: string | null
} & Record<string, unknown>

export type WorkflowCanvasEdgeData = {
  branchLabel: string | null
  typeContract: string | null
} & Record<string, unknown>

export type WorkflowCanvasNode = Node<WorkflowCanvasNodeData>
export type WorkflowCanvasEdge = Edge<WorkflowCanvasEdgeData>

interface HistorySnapshot {
  nodes: WorkflowCanvasNode[]
  edges: WorkflowCanvasEdge[]
  variables: WorkflowDraftVariableDefinition[]
}

const MAX_HISTORY = 50

function generateNodeKey(nodeType: WorkflowNodeType): string {
  return `${nodeType.toLowerCase()}_${Math.random().toString(36).slice(2, 8)}`
}

function toCanvasNode(draftNode: WorkflowDraftDefinition['nodes'][number]): WorkflowCanvasNode {
  const { canvasX, canvasY, ...data } = draftNode
  return { id: draftNode.nodeKey, type: 'workflowNode', position: { x: canvasX, y: canvasY }, data }
}

function toCanvasEdge(connection: WorkflowDraftDefinition['connections'][number], index: number): WorkflowCanvasEdge {
  return {
    id: `${connection.sourceNodeKey}->${connection.targetNodeKey}-${index}`,
    source: connection.sourceNodeKey,
    target: connection.targetNodeKey,
    data: { branchLabel: connection.branchLabel, typeContract: connection.typeContract },
  }
}

interface WorkflowCanvasState {
  nodes: WorkflowCanvasNode[]
  edges: WorkflowCanvasEdge[]
  variables: WorkflowDraftVariableDefinition[]
  inputsSchemaJson: string
  outputsSchemaJson: string
  errorPolicyJson: string
  selectedNodeId: string | null
  isDirty: boolean
  past: HistorySnapshot[]
  future: HistorySnapshot[]

  loadDefinition: (definition: WorkflowDraftDefinition) => void
  toDraftDefinition: () => WorkflowDraftDefinition
  reset: () => void

  onNodesChange: (changes: NodeChange<WorkflowCanvasNode>[]) => void
  onEdgesChange: (changes: EdgeChange<WorkflowCanvasEdge>[]) => void
  onConnect: (connection: Connection) => void
  onNodeDragStop: () => void

  addNode: (nodeType: WorkflowNodeType, position: XYPosition) => void
  updateNodeData: (nodeId: string, patch: Partial<WorkflowCanvasNodeData>) => void
  removeNodes: (nodeIds: string[]) => void
  duplicateNode: (nodeId: string) => void
  /** Ctrl+V — each pasted node keeps its copied configuration but gets a fresh `nodeKey`/id and a small position offset, mirroring {@link duplicateNode}. */
  pasteNodes: (nodesData: WorkflowCanvasNodeData[]) => void

  setSelectedNodeId: (id: string | null) => void
  setVariables: (variables: WorkflowDraftVariableDefinition[]) => void
  addVariable: (name: string, valueType: WorkflowVariableType, kind: WorkflowVariableKind) => void
  removeVariable: (name: string) => void
  setInputsSchemaJson: (json: string) => void
  setOutputsSchemaJson: (json: string) => void
  setErrorPolicyJson: (json: string) => void

  undo: () => void
  redo: () => void
  autoLayout: () => void
  markSaved: () => void
}

export const useWorkflowCanvasStore = create<WorkflowCanvasState>()((set, get) => {
  function snapshot(): HistorySnapshot {
    const { nodes, edges, variables } = get()
    return { nodes, edges, variables }
  }

  function pushHistory() {
    set((state) => ({
      past: [...state.past, snapshot()].slice(-MAX_HISTORY),
      future: [],
    }))
  }

  return {
    nodes: [],
    edges: [],
    variables: [],
    inputsSchemaJson: '{}',
    outputsSchemaJson: '{}',
    errorPolicyJson: '{"strategy":"Stop"}',
    selectedNodeId: null,
    isDirty: false,
    past: [],
    future: [],

    loadDefinition: (definition) =>
      set({
        nodes: definition.nodes.map(toCanvasNode),
        edges: definition.connections.map(toCanvasEdge),
        variables: definition.variables,
        inputsSchemaJson: definition.inputsSchemaJson,
        outputsSchemaJson: definition.outputsSchemaJson,
        errorPolicyJson: definition.errorPolicyJson,
        selectedNodeId: null,
        isDirty: false,
        past: [],
        future: [],
      }),

    toDraftDefinition: () => {
      const { nodes, edges, variables, inputsSchemaJson, outputsSchemaJson, errorPolicyJson } = get()
      return {
        inputsSchemaJson,
        outputsSchemaJson,
        errorPolicyJson,
        executionPolicyJson: '{}',
        securityPolicyJson: '{}',
        nodes: nodes.map((node) => ({ ...node.data, canvasX: node.position.x, canvasY: node.position.y })),
        connections: edges.map((edge) => ({
          sourceNodeKey: edge.source,
          targetNodeKey: edge.target,
          branchLabel: edge.data?.branchLabel ?? null,
          typeContract: edge.data?.typeContract ?? null,
        })),
        variables,
      }
    },

    reset: () =>
      set({
        nodes: [],
        edges: [],
        variables: [],
        inputsSchemaJson: EMPTY_DRAFT_DEFINITION.inputsSchemaJson,
        outputsSchemaJson: EMPTY_DRAFT_DEFINITION.outputsSchemaJson,
        errorPolicyJson: EMPTY_DRAFT_DEFINITION.errorPolicyJson,
        selectedNodeId: null,
        isDirty: false,
        past: [],
        future: [],
      }),

    // Position-drag changes fire continuously; history is snapshotted once at drag-start via
    // onNodeDragStop's counterpart (the drag's *first* change already moved the node by the time
    // this handler sees it, so selection-only churn — not worth an undo step — is filtered here
    // and drag committal is handled by onNodeDragStop instead).
    onNodesChange: (changes) =>
      set((state) => {
        const isDirtyingChange = changes.some((c) => c.type !== 'select' && c.type !== 'dimensions')
        return { nodes: applyNodeChanges(changes, state.nodes), isDirty: state.isDirty || isDirtyingChange }
      }),

    onEdgesChange: (changes) => {
      const isDirtyingChange = changes.some((c) => c.type !== 'select')
      if (isDirtyingChange) pushHistory()
      set((state) => ({ edges: applyEdgeChanges(changes, state.edges), isDirty: state.isDirty || isDirtyingChange }))
    },

    onConnect: (connection) => {
      if (!connection.source || !connection.target) return
      pushHistory()
      set((state) => ({
        edges: [
          ...state.edges,
          { id: `${connection.source}->${connection.target}-${state.edges.length}`, source: connection.source!, target: connection.target!, data: { branchLabel: null, typeContract: null } },
        ],
        isDirty: true,
      }))
    },

    onNodeDragStop: () => pushHistory(),

    addNode: (nodeType, position) => {
      pushHistory()
      const entry = getNodeCatalogEntry(nodeType)
      const nodeKey = generateNodeKey(nodeType)
      const node: WorkflowCanvasNode = {
        id: nodeKey,
        type: 'workflowNode',
        position,
        data: {
          nodeKey,
          nodeType,
          name: entry.label,
          description: null,
          inputSchemaJson: entry.defaultInputSchemaJson,
          outputSchemaJson: entry.defaultOutputSchemaJson,
          configurationJson: entry.defaultConfigurationJson,
          requiredPermissionsJson: '[]',
          timeoutSeconds: null,
          retryPolicyJson: null,
          approvalPolicy: 'NeverRequire',
          idempotencyKeyExpression: null,
          compensatingNodeKey: null,
        },
      }
      set((state) => ({ nodes: [...state.nodes, node], selectedNodeId: nodeKey, isDirty: true }))
    },

    updateNodeData: (nodeId, patch) => {
      pushHistory()
      set((state) => ({
        nodes: state.nodes.map((node) => (node.id === nodeId ? { ...node, data: { ...node.data, ...patch } } : node)),
        isDirty: true,
      }))
    },

    removeNodes: (nodeIds) => {
      pushHistory()
      const idSet = new Set(nodeIds)
      set((state) => ({
        nodes: state.nodes.filter((n) => !idSet.has(n.id)),
        edges: state.edges.filter((e) => !idSet.has(e.source) && !idSet.has(e.target)),
        selectedNodeId: idSet.has(state.selectedNodeId ?? '') ? null : state.selectedNodeId,
        isDirty: true,
      }))
    },

    duplicateNode: (nodeId) => {
      const source = get().nodes.find((n) => n.id === nodeId)
      if (!source) return
      pushHistory()
      const nodeKey = generateNodeKey(source.data.nodeType)
      const duplicate: WorkflowCanvasNode = {
        id: nodeKey,
        type: 'workflowNode',
        position: { x: source.position.x + 40, y: source.position.y + 40 },
        data: { ...source.data, nodeKey, name: `${source.data.name} (copy)` },
      }
      set((state) => ({ nodes: [...state.nodes, duplicate], selectedNodeId: nodeKey, isDirty: true }))
    },

    pasteNodes: (nodesData) => {
      if (nodesData.length === 0) return
      pushHistory()
      const newNodes: WorkflowCanvasNode[] = nodesData.map((data) => {
        const nodeKey = generateNodeKey(data.nodeType)
        return { id: nodeKey, type: 'workflowNode', position: { x: 60, y: 60 }, data: { ...data, nodeKey } }
      })
      set((state) => ({
        nodes: [...state.nodes, ...newNodes],
        selectedNodeId: newNodes[newNodes.length - 1]?.id ?? state.selectedNodeId,
        isDirty: true,
      }))
    },

    setSelectedNodeId: (id) => set({ selectedNodeId: id }),

    setVariables: (variables) => {
      pushHistory()
      set({ variables, isDirty: true })
    },

    addVariable: (name, valueType, kind) => {
      pushHistory()
      set((state) => ({
        variables: [...state.variables, { name, kind, valueType, defaultValueJson: null, isRequired: false }],
        isDirty: true,
      }))
    },

    removeVariable: (name) => {
      pushHistory()
      set((state) => ({ variables: state.variables.filter((v) => v.name !== name), isDirty: true }))
    },

    setInputsSchemaJson: (json) => set({ inputsSchemaJson: json, isDirty: true }),
    setOutputsSchemaJson: (json) => set({ outputsSchemaJson: json, isDirty: true }),
    setErrorPolicyJson: (json) => set({ errorPolicyJson: json, isDirty: true }),

    undo: () => {
      const { past } = get()
      if (past.length === 0) return
      const previous = past[past.length - 1]
      set((state) => ({
        nodes: previous.nodes,
        edges: previous.edges,
        variables: previous.variables,
        past: state.past.slice(0, -1),
        future: [snapshot(), ...state.future],
        isDirty: true,
      }))
    },

    redo: () => {
      const { future } = get()
      if (future.length === 0) return
      const next = future[0]
      set((state) => ({
        nodes: next.nodes,
        edges: next.edges,
        variables: next.variables,
        past: [...state.past, snapshot()],
        future: state.future.slice(1),
        isDirty: true,
      }))
    },

    // Layered left-to-right layout by longest-path depth from the Start node (Kahn's algorithm,
    // mirroring the same topological approach WorkflowExecutionOrchestrator uses server-side) —
    // hand-rolled rather than a new npm dependency (dagre/elkjs) for a placement this simple.
    autoLayout: () => {
      pushHistory()
      const { nodes, edges } = get()
      const depthByNodeId = new Map<string, number>()
      const incoming = new Map<string, number>()
      nodes.forEach((n) => incoming.set(n.id, 0))
      edges.forEach((e) => incoming.set(e.target, (incoming.get(e.target) ?? 0) + 1))

      const queue = nodes.filter((n) => (incoming.get(n.id) ?? 0) === 0).map((n) => n.id)
      queue.forEach((id) => depthByNodeId.set(id, 0))
      const remaining = new Map(incoming)

      while (queue.length > 0) {
        const currentId = queue.shift()!
        const currentDepth = depthByNodeId.get(currentId) ?? 0
        for (const edge of edges.filter((e) => e.source === currentId)) {
          depthByNodeId.set(edge.target, Math.max(depthByNodeId.get(edge.target) ?? 0, currentDepth + 1))
          const remainingCount = (remaining.get(edge.target) ?? 1) - 1
          remaining.set(edge.target, remainingCount)
          if (remainingCount === 0) queue.push(edge.target)
        }
      }

      const columnCounts = new Map<number, number>()
      const COLUMN_WIDTH = 260
      const ROW_HEIGHT = 140

      const laidOutNodes = nodes.map((node) => {
        const depth = depthByNodeId.get(node.id) ?? 0
        const row = columnCounts.get(depth) ?? 0
        columnCounts.set(depth, row + 1)
        return { ...node, position: { x: depth * COLUMN_WIDTH, y: row * ROW_HEIGHT } }
      })

      set({ nodes: laidOutNodes, isDirty: true })
    },

    markSaved: () => set({ isDirty: false }),
  }
})
