import { ActionIcon, Badge, Group, Tooltip, useComputedColorScheme, useMantineColorScheme } from '@mantine/core'
import { useLocalStorage, useReducedMotion } from '@mantine/hooks'
import { useEffect, useRef, useState } from 'react'
import { Link, Navigate, NavLink, Route, Routes, useLocation } from 'react-router'
import { useLiveUpdates, useOverview, useRealtimeStatus } from './api/hooks'
import type { RealtimeStatus } from './api/realtime'
import { PanelIcon } from './components/PanelIcon'
import { CoordinatorsPage } from './pages/CoordinatorsPage'
import { SfuNodesPage } from './pages/SfuNodesPage'
import { CoordinatorPage } from './pages/CoordinatorPage'
import { CoordinatorSettingsPage } from './pages/CoordinatorSettingsPage'
import { OverviewPage } from './pages/OverviewPage'
import { BansPage } from './pages/BansPage'
import { RolesPage } from './pages/RolesPage'

export function App() {
  useLiveUpdates()
  const [menuOpen, setMenuOpen] = useState(false)
  const [sidebarCollapsed, setSidebarCollapsed] = useLocalStorage({ key: 'flowrate-panel-sidebar-collapsed', defaultValue: false })
  const reducedMotion = useReducedMotion(undefined, { getInitialValueInEffect: false })
  const contentRef = useRef<HTMLElement>(null)
  const overview = useOverview()
  const location = useLocation()
  useEffect(() => {
    if (reducedMotion) return
    const animation = contentRef.current?.animate(
      [{ opacity: 0, transform: 'translateY(8px)' }, { opacity: 1, transform: 'translateY(0)' }],
      { duration: 240, easing: 'cubic-bezier(.2, .8, .2, 1)' },
    )
    return () => animation?.cancel()
  }, [location.pathname, reducedMotion])

  useEffect(() => {
    if (!menuOpen) return
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false)
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [menuOpen])

  const coordinatorId = location.pathname.match(/^\/coordinators\/([^/]+)/)?.[1]
  const coordinator = overview.data?.coordinators.find(item => item.id === coordinatorId)
  const section = location.pathname.endsWith('/bans') ? 'Global bans' : location.pathname.endsWith('/settings') ? 'Settings' : location.pathname.endsWith('/roles') ? 'Roles & access' : coordinatorId ? 'Coordinator' : location.pathname === '/coordinators' ? 'Coordinators' : location.pathname === '/sfu-nodes' ? 'SFU nodes' : 'Statistics'

  return (
    <div className={`panel-shell ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`}>
      <button className={`nav-backdrop ${menuOpen ? 'is-open' : ''}`} aria-label="Close navigation" aria-hidden={!menuOpen} tabIndex={menuOpen ? 0 : -1} onClick={() => setMenuOpen(false)} />
      <aside id="panel-navigation" className={`panel-sidebar ${menuOpen ? 'is-open' : ''}`}>
        <Link to="/" className="brand" aria-label="FlowRate home" title="FlowRate home" onClick={() => setMenuOpen(false)}>
          <span className="brand-mark"><PanelIcon name="activity" size={25} /></span>
          <span className="brand-text">FlowRate<span className="brand-subtitle">SERVER CONSOLE</span></span>
        </Link>
        <div className="nav-label">WORKSPACE</div>
        <nav aria-label="Main navigation" onClick={() => setMenuOpen(false)}>
          <NavLink to="/" end className="nav-link" aria-label="Statistics" title="Statistics"><PanelIcon name="overview" /><span className="nav-text">Statistics</span></NavLink>
          <NavLink to="/coordinators" className="nav-link" aria-label="Coordinators" title="Coordinators"><PanelIcon name="server" /><span className="nav-text">Coordinators</span><span className="nav-count">{overview.data?.coordinators.length ?? '—'}</span></NavLink>
          <NavLink to="/sfu-nodes" className="nav-link" aria-label="SFU nodes" title="SFU nodes"><PanelIcon name="nodes" /><span className="nav-text">SFU nodes</span></NavLink>
          {coordinatorId && <>
            <div className="nav-label coordinator-label">COORDINATOR</div>
            <NavLink to={`/coordinators/${coordinatorId}`} end className="nav-link" aria-label="Activity" title="Activity"><PanelIcon name="activity" /><span className="nav-text">Activity</span></NavLink>
            <NavLink to={`/coordinators/${coordinatorId}/roles`} className="nav-link" aria-label="Roles & access" title="Roles & access"><PanelIcon name="shield" /><span className="nav-text">Roles & access</span></NavLink>
            <NavLink to={`/coordinators/${coordinatorId}/bans`} className="nav-link" aria-label="Global bans" title="Global bans"><PanelIcon name="shield" /><span className="nav-text">Global bans</span></NavLink>
            <NavLink to={`/coordinators/${coordinatorId}/settings`} className="nav-link" aria-label="Settings" title="Settings"><PanelIcon name="settings" /><span className="nav-text">Settings</span></NavLink>
          </>}
        </nav>
        <div className="sidebar-bottom">
          <div className="sidebar-version"><span className="status-dot" /><span className="sidebar-version-text">FlowRate</span>{overview.data && <span className="sidebar-version-text">v{overview.data.serverVersion}</span>}</div>
        </div>
      </aside>
      <div className="panel-workspace">
        <header className="panel-topbar">
          <Group gap="sm" wrap="nowrap">
            <Tooltip label={sidebarCollapsed ? 'Expand navigation' : 'Collapse navigation'}><ActionIcon className="desktop-menu" variant="subtle" color="gray" aria-label={sidebarCollapsed ? 'Expand navigation' : 'Collapse navigation'} aria-expanded={!sidebarCollapsed} aria-controls="panel-navigation" onClick={() => setSidebarCollapsed(value => !value)}><PanelIcon name="arrow" style={{ transform: sidebarCollapsed ? 'rotate(0deg)' : 'rotate(180deg)' }} /></ActionIcon></Tooltip>
            <ActionIcon className="mobile-menu" variant="subtle" color="gray" aria-label="Open navigation" aria-expanded={menuOpen} aria-controls="panel-navigation" onClick={() => setMenuOpen(true)}><PanelIcon name="menu" /></ActionIcon>
            <span className="breadcrumb-root">Workspace</span><span className="breadcrumb-slash">/</span>
            {coordinator && <><Link className="breadcrumb-coordinator" to={`/coordinators/${coordinatorId}`}>{coordinator.name}</Link><span className="breadcrumb-slash">/</span></>}
            <span className="breadcrumb-current">{section}</span>
          </Group>
          <Group gap="md" wrap="nowrap"><LiveBadge /><span className="topbar-divider" /><ColorSchemeToggle /></Group>
        </header>
        <main ref={contentRef} className="panel-content" id="main-content">
          <Routes>
            <Route index element={<OverviewPage />} />
            <Route path="coordinators" element={<CoordinatorsPage />} />
            <Route path="sfu-nodes" element={<SfuNodesPage />} />
            <Route path="coordinators/:id" element={<CoordinatorPage />} />
            <Route path="coordinators/:id/settings" element={<CoordinatorSettingsPage />} />
            <Route path="coordinators/:id/bans" element={<BansPage />} />
            <Route path="coordinators/:id/roles" element={<RolesPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
        <footer className="panel-footer"><span>FlowRate server console</span><span>Built for real-time connections</span></footer>
      </div>
    </div>
  )
}

const liveBadges: Record<RealtimeStatus, { color: string, label: string, hint: string }> = {
  open: { color: 'green', label: 'Live updates', hint: 'Changes appear as they happen' },
  connecting: { color: 'gray', label: 'Connecting', hint: 'Opening the live updates stream' },
  reconnecting: { color: 'yellow', label: 'Reconnecting', hint: 'Live updates are interrupted; retrying' },
}

function LiveBadge() {
  const badge = liveBadges[useRealtimeStatus()]
  return <Tooltip label={badge.hint}><Badge color={badge.color} variant="dot" className="live-badge">{badge.label}</Badge></Tooltip>
}

function ColorSchemeToggle() {
  const { setColorScheme } = useMantineColorScheme()
  const scheme = useComputedColorScheme('dark')
  const next = scheme === 'dark' ? 'light' : 'dark'
  return <Tooltip label={`Switch to ${next} theme`}><ActionIcon variant="subtle" color="gray" size="lg" aria-label={`Switch to ${next} theme`} onClick={() => setColorScheme(next)}><PanelIcon name={scheme === 'dark' ? 'sun' : 'moon'} /></ActionIcon></Tooltip>
}
