import {
  RiAdminLine,
  RiArrowLeftRightLine,
  RiArticleLine,
  RiBankLine,
  RiBox3Line,
  RiBrush2Line,
  RiBuilding2Line,
  RiCompassLine,
  RiCursorLine,
  RiDropLine,
  RiFileTextLine,
  RiFilterLine,
  RiFingerprintLine,
  RiFlashlightLine,
  RiFlowChart,
  RiFocus3Line,
  RiFolderLine,
  RiFullscreenLine,
  RiGpsLine,
  RiGridLine,
  RiGroupLine,
  RiHistoryLine,
  RiLineChartLine,
  RiLogoutBoxLine,
  RiMapLine,
  RiNavigationLine,
  RiRobotLine,
  RiRouteLine,
  RiSettings3Line,
  RiShieldCheckLine,
  RiShoppingCartLine,
  RiStackLine,
  RiSunLine,
  RiEqualizerLine,
  RiUserLine,
  RiUserSettingsLine,
  RiBrainLine,
} from '@remixicon/react'
import { useNavigate } from 'react-router'
import {
  ExpandableActionGroup,
  type ExpandableActionGroupAction,
} from '../../components/workspace-shell/ExpandableActionGroup'
import type { ControlDefinition } from '../../components/workspace-shell/types'
import { useLogout } from '../auth/hooks/useAuth'
import { useIsAdmin } from '../../hooks/useIsAdmin'
import { useComingSoonStore } from '../../store/comingSoonStore'
import { useWorkspaceOverlayStore, type ViewMode } from '../../store/workspaceOverlayStore'
import { viewerEngine } from '../../viewer/engine/viewerEngineInstance'
import { SETTINGS_TAB_INDEX } from '../settings/settingsTabs'

function comingSoon(label: string) {
  useComingSoonStore.getState().show(label)
}

/** FR-024: preserves every destination reachable from the existing account menu
 * (`UserMenu.tsx`, mounted elsewhere via `AppShell`) — the theme toggle is now its own
 * separate top-cluster button (readdy.ai reference), not one of these actions. This
 * list intentionally mirrors `UserMenu.tsx`'s destinations rather than importing it
 * directly (that component is built around a MUI `Menu` popover anchored to a button,
 * not an `ExpandableActionGroup`'s in-place list) — keep the two in sync if a
 * destination is added to or removed from either. */
export function useAccountControl(): ControlDefinition {
  const navigate = useNavigate()
  const isAdmin = useIsAdmin()
  const logout = useLogout()

  const actions: ExpandableActionGroupAction[] = [
    {
      id: 'profile',
      label: 'Profile',
      icon: <RiUserLine size={20} />,
      onSelect: () => navigate('/profile'),
    },
    {
      id: 'settings',
      label: 'Settings',
      icon: <RiSettings3Line size={20} />,
      onSelect: () => navigate('/settings'),
    },
    {
      id: 'chat-configuration',
      label: 'Chat Configuration',
      icon: <RiEqualizerLine size={20} />,
      onSelect: () =>
        navigate('/settings', { state: { tab: SETTINGS_TAB_INDEX.ChatConfiguration } }),
    },
    {
      id: 'chat-history',
      label: 'Chat History',
      icon: <RiHistoryLine size={20} />,
      onSelect: () => navigate('/settings', { state: { tab: SETTINGS_TAB_INDEX.ChatHistory } }),
    },
    {
      id: 'documents',
      label: 'Documents',
      icon: <RiFileTextLine size={20} />,
      onSelect: () => navigate('/documents'),
    },
    {
      id: 'knowledge-bases',
      label: 'Knowledge Bases',
      icon: <RiFolderLine size={20} />,
      onSelect: () => navigate('/knowledge-bases'),
    },
    {
      id: 'memory',
      label: 'Memory Center',
      icon: <RiBrainLine size={20} />,
      onSelect: () => navigate('/memory'),
    },
    {
      id: 'prompts',
      label: 'Prompts',
      icon: <RiArticleLine size={20} />,
      onSelect: () => navigate('/prompts'),
    },
    {
      id: 'agents',
      label: 'Agents',
      icon: <RiRobotLine size={20} />,
      onSelect: () => navigate('/agents'),
    },
    {
      id: 'workflows',
      label: 'Workflows',
      icon: <RiFlowChart size={20} />,
      onSelect: () => navigate('/workflows'),
    },
    ...(isAdmin
      ? [
          {
            id: 'admin',
            label: 'Admin panel',
            icon: <RiAdminLine size={20} />,
            onSelect: () => navigate('/admin/dashboard'),
          },
        ]
      : []),
    {
      id: 'privacy',
      label: 'Privacy Policy',
      icon: <RiShieldCheckLine size={20} />,
      onSelect: () => navigate('/privacy'),
    },
    {
      id: 'logout',
      label: 'Log out',
      icon: <RiLogoutBoxLine size={20} />,
      onSelect: () =>
        logout.mutate(undefined, { onSuccess: () => navigate('/', { replace: true }) }),
    },
  ]

  return {
    id: 'account',
    label: 'Account',
    icon: <RiUserSettingsLine />,
    status: 'functional',
    kind: 'action-group',
    placement: 'top-cluster',
    content: <ExpandableActionGroup layout="list" actions={actions} />,
  }
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
