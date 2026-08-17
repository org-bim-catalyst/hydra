import AccountBalanceOutlinedIcon from '@mui/icons-material/AccountBalanceOutlined'
import AdminPanelSettingsIcon from '@mui/icons-material/AdminPanelSettings'
import AltRouteOutlinedIcon from '@mui/icons-material/AltRouteOutlined'
import ArticleOutlinedIcon from '@mui/icons-material/ArticleOutlined'
import BoltOutlinedIcon from '@mui/icons-material/BoltOutlined'
import CompareArrowsOutlinedIcon from '@mui/icons-material/CompareArrowsOutlined'
import CropFreeOutlinedIcon from '@mui/icons-material/CropFreeOutlined'
import DescriptionIcon from '@mui/icons-material/Description'
import ExploreOutlinedIcon from '@mui/icons-material/ExploreOutlined'
import FilterAltOutlinedIcon from '@mui/icons-material/FilterAltOutlined'
import FolderIcon from '@mui/icons-material/Folder'
import GestureOutlinedIcon from '@mui/icons-material/GestureOutlined'
import GridViewOutlinedIcon from '@mui/icons-material/GridViewOutlined'
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined'
import HighlightAltIcon from '@mui/icons-material/HighlightAlt'
import HistoryOutlinedIcon from '@mui/icons-material/HistoryOutlined'
import InsightsIcon from '@mui/icons-material/Insights'
import LayersIcon from '@mui/icons-material/Layers'
import LocationCityOutlinedIcon from '@mui/icons-material/LocationCityOutlined'
import LogoutIcon from '@mui/icons-material/Logout'
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts'
import MapOutlinedIcon from '@mui/icons-material/MapOutlined'
import MyLocationOutlinedIcon from '@mui/icons-material/MyLocationOutlined'
import NavigationIcon from '@mui/icons-material/Navigation'
import PersonIcon from '@mui/icons-material/Person'
import PolicyIcon from '@mui/icons-material/Policy'
import PsychologyOutlinedIcon from '@mui/icons-material/PsychologyOutlined'
import SettingsIcon from '@mui/icons-material/Settings'
import ShoppingCartOutlinedIcon from '@mui/icons-material/ShoppingCartOutlined'
import SmartToyOutlinedIcon from '@mui/icons-material/SmartToyOutlined'
import ThreeDRotationIcon from '@mui/icons-material/ThreeDRotation'
import TouchAppOutlinedIcon from '@mui/icons-material/TouchAppOutlined'
import TuneOutlinedIcon from '@mui/icons-material/TuneOutlined'
import WaterDropOutlinedIcon from '@mui/icons-material/WaterDropOutlined'
import WbSunnyOutlinedIcon from '@mui/icons-material/WbSunnyOutlined'
import ZoomInMapOutlinedIcon from '@mui/icons-material/ZoomInMapOutlined'
import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined'
import { useNavigate } from 'react-router'
import { ExpandableActionGroup, type ExpandableActionGroupAction } from '../../components/workspace-shell/ExpandableActionGroup'
import type { ControlDefinition } from '../../components/workspace-shell/types'
import { useLogout } from '../auth/hooks/useAuth'
import { useIsAdmin } from '../../hooks/useIsAdmin'
import { useComingSoonStore } from '../../store/comingSoonStore'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
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
    { id: 'profile', label: 'Profile', icon: <PersonIcon fontSize="small" />, onSelect: () => navigate('/profile') },
    { id: 'settings', label: 'Settings', icon: <SettingsIcon fontSize="small" />, onSelect: () => navigate('/settings') },
    {
      id: 'chat-configuration',
      label: 'Chat Configuration',
      icon: <TuneOutlinedIcon fontSize="small" />,
      onSelect: () => navigate('/settings', { state: { tab: SETTINGS_TAB_INDEX.ChatConfiguration } }),
    },
    {
      id: 'chat-history',
      label: 'Chat History',
      icon: <HistoryOutlinedIcon fontSize="small" />,
      onSelect: () => navigate('/settings', { state: { tab: SETTINGS_TAB_INDEX.ChatHistory } }),
    },
    {
      id: 'documents',
      label: 'Documents',
      icon: <DescriptionIcon fontSize="small" />,
      onSelect: () => navigate('/documents'),
    },
    {
      id: 'knowledge-bases',
      label: 'Knowledge Bases',
      icon: <FolderIcon fontSize="small" />,
      onSelect: () => navigate('/knowledge-bases'),
    },
    {
      id: 'memory',
      label: 'Memory Center',
      icon: <PsychologyOutlinedIcon fontSize="small" />,
      onSelect: () => navigate('/memory'),
    },
    { id: 'prompts', label: 'Prompts', icon: <ArticleOutlinedIcon fontSize="small" />, onSelect: () => navigate('/prompts') },
    {
      id: 'agents',
      label: 'Agents',
      icon: <SmartToyOutlinedIcon fontSize="small" />,
      onSelect: () => navigate('/agents'),
    },
    {
      id: 'workflows',
      label: 'Workflows',
      icon: <AccountTreeOutlinedIcon fontSize="small" />,
      onSelect: () => navigate('/workflows'),
    },
    ...(isAdmin
      ? [
          {
            id: 'admin',
            label: 'Admin panel',
            icon: <AdminPanelSettingsIcon fontSize="small" />,
            onSelect: () => navigate('/admin/dashboard'),
          },
        ]
      : []),
    { id: 'privacy', label: 'Privacy Policy', icon: <PolicyIcon fontSize="small" />, onSelect: () => navigate('/privacy') },
    {
      id: 'logout',
      label: 'Log out',
      icon: <LogoutIcon fontSize="small" />,
      onSelect: () => logout.mutate(undefined, { onSuccess: () => navigate('/', { replace: true }) }),
    },
  ]

  return {
    id: 'account',
    label: 'Account',
    icon: <ManageAccountsIcon />,
    status: 'functional',
    kind: 'action-group',
    placement: 'top-cluster',
    content: <ExpandableActionGroup layout="list" actions={actions} />,
  }
}

/** FR-010/FR-011: the one tool control this feature makes fully functional — selecting a
 * mode calls `workspaceOverlayStore.setViewMode`, which `WorkspaceSurface` reads to
 * visibly reflect the active mode (research.md #10). */
export function useViewModeControl(): ControlDefinition {
  const viewMode = useWorkspaceOverlayStore((s) => s.viewMode)
  const setViewMode = useWorkspaceOverlayStore((s) => s.setViewMode)

  const actions: ExpandableActionGroupAction[] = [
    {
      id: '2d',
      label: '2D',
      icon: <MapOutlinedIcon fontSize="small" />,
      onSelect: () => setViewMode('2D'),
      highlighted: viewMode === '2D',
    },
    {
      id: '3d',
      label: '3D',
      icon: <ThreeDRotationIcon fontSize="small" />,
      onSelect: () => setViewMode('3D'),
      highlighted: viewMode === '3D',
    },
  ]

  return {
    id: 'view-mode',
    label: 'View mode',
    icon: <ThreeDRotationIcon />,
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
  icon: <LayersIcon />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        { id: 'base-map', label: 'Base map', icon: <MapOutlinedIcon fontSize="small" />, onSelect: () => comingSoon('Layers') },
        {
          id: 'amenities',
          label: 'Amenities',
          icon: <ShoppingCartOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Layers'),
        },
        {
          id: 'buildings',
          label: 'Buildings',
          icon: <LocationCityOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Layers'),
        },
        {
          id: 'landmarks',
          label: 'Landmarks',
          icon: <AccountBalanceOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Layers'),
        },
        {
          id: 'layer-settings',
          label: 'Layer settings',
          icon: <TuneOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Layers'),
        },
      ]}
    />
  ),
}

export const navigationControl: ControlDefinition = {
  id: 'navigation',
  label: 'Navigation',
  icon: <NavigationIcon />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        { id: 'explore', label: 'Explore', icon: <ExploreOutlinedIcon fontSize="small" />, onSelect: () => comingSoon('Navigation') },
        {
          id: 'my-location',
          label: 'My location',
          icon: <MyLocationOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Navigation'),
        },
        {
          id: 'route',
          label: 'Route',
          icon: <AltRouteOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Navigation'),
        },
        {
          id: 'zoom-to-fit',
          label: 'Zoom to fit',
          icon: <ZoomInMapOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Navigation'),
        },
      ]}
    />
  ),
}

export const selectionControl: ControlDefinition = {
  id: 'selection',
  label: 'Selection',
  icon: <HighlightAltIcon />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        {
          id: 'marquee',
          label: 'Marquee select',
          icon: <CropFreeOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Selection'),
        },
        { id: 'tap', label: 'Tap select', icon: <TouchAppOutlinedIcon fontSize="small" />, onSelect: () => comingSoon('Selection') },
        {
          id: 'freehand',
          label: 'Freehand select',
          icon: <GestureOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Selection'),
        },
        {
          id: 'filter',
          label: 'Filter selection',
          icon: <FilterAltOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Selection'),
        },
      ]}
    />
  ),
}

export const analysisControl: ControlDefinition = {
  id: 'analysis',
  label: 'Analysis',
  icon: <InsightsIcon />,
  status: 'functional',
  kind: 'action-group',
  placement: 'right-stack',
  content: (
    <ExpandableActionGroup
      actions={[
        { id: 'sunlight', label: 'Sunlight', icon: <WbSunnyOutlinedIcon fontSize="small" />, onSelect: () => comingSoon('Analysis') },
        {
          id: 'compare',
          label: 'Compare scenarios',
          icon: <CompareArrowsOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Analysis'),
        },
        {
          id: 'density',
          label: 'Population density',
          icon: <GroupsOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Analysis'),
        },
        {
          id: 'drainage',
          label: 'Drainage',
          icon: <WaterDropOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Analysis'),
        },
        { id: 'grid', label: 'Grid overlay', icon: <GridViewOutlinedIcon fontSize="small" />, onSelect: () => comingSoon('Analysis') },
        {
          id: 'run',
          label: 'Run analysis',
          icon: <BoltOutlinedIcon fontSize="small" />,
          onSelect: () => comingSoon('Analysis'),
          highlighted: true,
        },
      ]}
    />
  ),
}
