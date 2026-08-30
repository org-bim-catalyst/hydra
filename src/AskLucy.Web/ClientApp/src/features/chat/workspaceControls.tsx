import {
  RiArrowLeftRightLine,
  RiBankLine,
  RiBox3Line,
  RiBrush2Line,
  RiBuilding2Line,
  RiCompassLine,
  RiCursorLine,
  RiDropLine,
  RiFilterLine,
  RiFingerprintLine,
  RiFlashlightLine,
  RiFocus3Line,
  RiFullscreenLine,
  RiGpsLine,
  RiGridLine,
  RiGroupLine,
  RiLineChartLine,
  RiMapLine,
  RiNavigationLine,
  RiPlanetLine,
  RiRoadMapLine,
  RiRouteLine,
  RiShoppingCartLine,
  RiStackLine,
  RiStackedView,
  RiSunLine,
  RiEqualizerLine,
} from '@remixicon/react'
import {
  ExpandableActionGroup,
  type ExpandableActionGroupAction,
} from '../../components/workspace-shell/ExpandableActionGroup'
import type { ControlDefinition } from '../../components/workspace-shell/types'
import { useComingSoonStore } from '../../store/comingSoonStore'
import { useWorkspaceOverlayStore, type ViewMode } from '../../store/workspaceOverlayStore'
import { viewerEngine } from '../../viewer/engine/viewerEngineInstance'
import { useViewerEngineStore } from '../../viewer/store/viewerEngineStore'
import type { MapStyleId } from '../../viewer/api/commands'

function comingSoon(label: string) {
  useComingSoonStore.getState().show(label)
}

/** specs/027-immersive-viewer-platform FR-013 (research.md Decision 4): repurposes what was
 * originally a cosmetic 2D/3D gradient toggle (SPEC-024) into the real viewer's isometric/plan
 * camera-perspective control — selecting a mode calls both `workspaceOverlayStore.setViewMode`
 * (so this control's own highlighted state stays in sync) and `viewerEngine.setViewMode` (the
 * command that actually moves the camera, per contracts/viewer-engine-api.md). */
export function useViewModeControl(): ControlDefinition {
  const viewMode = useWorkspaceOverlayStore((s) => s.viewMode)
  const setViewMode = useWorkspaceOverlayStore((s) => s.setViewMode)

  const selectMode = (mode: ViewMode) => {
    setViewMode(mode)
    viewerEngine.setViewMode(mode)
  }

  const actions: ExpandableActionGroupAction[] = [
    {
      id: 'isometric',
      label: 'Isometric',
      icon: <RiBox3Line size={20} />,
      onSelect: () => selectMode('isometric'),
      highlighted: viewMode === 'isometric',
    },
    {
      id: 'plan',
      label: 'Plan',
      icon: <RiMapLine size={20} />,
      onSelect: () => selectMode('plan'),
      highlighted: viewMode === 'plan',
    },
  ]

  return {
    id: 'view-mode',
    label: 'View mode',
    icon: <RiBox3Line />,
    status: 'functional',
    kind: 'action-group',
    placement: 'right-stack',
    content: <ExpandableActionGroup actions={actions} />,
  }
}

/** Map/GIS content mode's base rendering style — ROADMAP/SATELLITE/HYBRID, one ribbon of
 * icon actions matching the readdy.ai reference's Layers/Analysis row pattern. Reads
 * `viewerEngineStore.mapStyle` directly (rather than mirroring it into `workspaceOverlayStore`
 * the way `useViewModeControl` mirrors camera.mode into `viewMode`) — that's the one piece of
 * state a control actually needs to highlight its active action, so there is no reason to
 * duplicate it into a second store. `viewerEngine.setMapStyle` is the single source of truth
 * that both updates the store and forwards the change to the live Google Map (`MapRenderTarget`
 * → `GoogleMapsGisLayer.setMapTypeId` → `map.setMapTypeId(google.maps.MapTypeId.*)`). */
export function useMapStyleControl(): ControlDefinition {
  const mapStyle = useViewerEngineStore((s) => s.mapStyle)

  const selectStyle = (style: MapStyleId) => {
    viewerEngine.setMapStyle(style)
  }

  const actions: ExpandableActionGroupAction[] = [
    {
      id: 'roadmap',
      label: 'Road map',
      icon: <RiRoadMapLine size={20} />,
      onSelect: () => selectStyle('roadmap'),
      highlighted: mapStyle === 'roadmap',
    },
    {
      id: 'satellite',
      label: 'Satellite',
      icon: <RiPlanetLine size={20} />,
      onSelect: () => selectStyle('satellite'),
      highlighted: mapStyle === 'satellite',
    },
    {
      id: 'hybrid',
      label: 'Hybrid',
      icon: <RiStackedView size={20} />,
      onSelect: () => selectStyle('hybrid'),
      highlighted: mapStyle === 'hybrid',
    },
  ]

  return {
    id: 'map-style',
    label: 'Map style',
    icon: <RiRoadMapLine />,
    status: 'functional',
    kind: 'action-group',
    placement: 'right-stack',
    content: <ExpandableActionGroup actions={actions} />,
  }
}

/** FR-012/FR-021: layers/navigation/selection/analysis are visible, reachable icon
 * controls, matching the readdy.ai reference's real icon rows — real functionality is
 * out of this feature's scope (delivered by later, separate features), so every action
 * here opens the shared "coming soon" dialog instead of doing the real thing
 * (research.md #6, revised to match the reference's icon-then-modal pattern rather than
 * an inline placeholder message). Static (no hooks needed), so these are plain
 * constants rather than hook functions like the account/view-mode controls above. */
export const layersControl: ControlDefinition = {
  id: 'layers',
  label: 'Layers',
  icon: <RiStackLine />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        {
          id: 'base-map',
          label: 'Base map',
          icon: <RiMapLine size={20} />,
          onSelect: () => comingSoon('Layers'),
        },
        {
          id: 'amenities',
          label: 'Amenities',
          icon: <RiShoppingCartLine size={20} />,
          onSelect: () => comingSoon('Layers'),
        },
        {
          id: 'buildings',
          label: 'Buildings',
          icon: <RiBuilding2Line size={20} />,
          onSelect: () => comingSoon('Layers'),
        },
        {
          id: 'landmarks',
          label: 'Landmarks',
          icon: <RiBankLine size={20} />,
          onSelect: () => comingSoon('Layers'),
        },
        {
          id: 'layer-settings',
          label: 'Layer settings',
          icon: <RiEqualizerLine size={20} />,
          onSelect: () => comingSoon('Layers'),
        },
      ]}
    />
  ),
}

export const navigationControl: ControlDefinition = {
  id: 'navigation',
  label: 'Navigation',
  icon: <RiNavigationLine />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        {
          id: 'explore',
          label: 'Explore',
          icon: <RiCompassLine size={20} />,
          onSelect: () => comingSoon('Navigation'),
        },
        {
          id: 'my-location',
          label: 'My location',
          icon: <RiGpsLine size={20} />,
          onSelect: () => comingSoon('Navigation'),
        },
        {
          id: 'route',
          label: 'Route',
          icon: <RiRouteLine size={20} />,
          onSelect: () => comingSoon('Navigation'),
        },
        {
          id: 'zoom-to-fit',
          label: 'Zoom to fit',
          icon: <RiFullscreenLine size={20} />,
          onSelect: () => comingSoon('Navigation'),
        },
      ]}
    />
  ),
}

export const selectionControl: ControlDefinition = {
  id: 'selection',
  label: 'Selection',
  icon: <RiCursorLine />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        {
          id: 'marquee',
          label: 'Marquee select',
          icon: <RiFocus3Line size={20} />,
          onSelect: () => comingSoon('Selection'),
        },
        {
          id: 'tap',
          label: 'Tap select',
          icon: <RiFingerprintLine size={20} />,
          onSelect: () => comingSoon('Selection'),
        },
        {
          id: 'freehand',
          label: 'Freehand select',
          icon: <RiBrush2Line size={20} />,
          onSelect: () => comingSoon('Selection'),
        },
        {
          id: 'filter',
          label: 'Filter selection',
          icon: <RiFilterLine size={20} />,
          onSelect: () => comingSoon('Selection'),
        },
      ]}
    />
  ),
}

export const analysisControl: ControlDefinition = {
  id: 'analysis',
  label: 'Analysis',
  icon: <RiLineChartLine />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        {
          id: 'sunlight',
          label: 'Sunlight',
          icon: <RiSunLine size={20} />,
          onSelect: () => comingSoon('Analysis'),
        },
        {
          id: 'compare',
          label: 'Compare scenarios',
          icon: <RiArrowLeftRightLine size={20} />,
          onSelect: () => comingSoon('Analysis'),
        },
        {
          id: 'density',
          label: 'Population density',
          icon: <RiGroupLine size={20} />,
          onSelect: () => comingSoon('Analysis'),
        },
        {
          id: 'drainage',
          label: 'Drainage',
          icon: <RiDropLine size={20} />,
          onSelect: () => comingSoon('Analysis'),
        },
        {
          id: 'grid',
          label: 'Grid overlay',
          icon: <RiGridLine size={20} />,
          onSelect: () => comingSoon('Analysis'),
        },
        {
          id: 'run',
          label: 'Run analysis',
          icon: <RiFlashlightLine size={20} />,
          onSelect: () => comingSoon('Analysis'),
          highlighted: true,
        },
      ]}
    />
  ),
}
