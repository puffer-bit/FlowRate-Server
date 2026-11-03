import { ActionIcon, Badge, Group, Tooltip, useComputedColorScheme, useMantineColorScheme } from '@mantine/core'
import { useState } from 'react'
import { Link, Navigate, NavLink, Route, Routes, useLocation } from 'react-router'
import { useLiveUpdates, useOverview, useRealtimeStatus } from './api/hooks'
import type { RealtimeStatus } from './api/realtime'
import { PanelIcon } from './components/PanelIcon'
import { CoordinatorsPage } from './pages/CoordinatorsPage'
import { SfuNodesPage } from './pages/SfuNodesPage'
import { CoordinatorPage } from './pages/CoordinatorPage'
import { CoordinatorSettingsPage } from './pages/CoordinatorSettingsPage'
import { OverviewPage } from './pages/OverviewPage'
import { RolesPage } from './pages/RolesPage'

export function App() {
  useLiveUpdates()
  const [menuOpen, setMenuOpen] = useState(false)
  const overview = useOverview()
  const location = useLocation()
  const coordinatorId = location.pathname.match(/^\/coordinators\/([^/]+)/)?.[1]
  const coordinator = overview.data?.coordinators.find(item => item.id === coordinatorId)
  const section = location.pathname.endsWith('/settings') ? 'Settings' : location.pathname.endsWith('/roles') ? 'Roles & access' : coordinatorId ? 'Coordinator' : location.pathname === '/coordinators' ? 'Coordinators' : location.pathname === '/sfu-nodes' ? 'SFU nodes' : 'Statistics'

  return (
    <div className="panel-shell">
      {menuOpen && <button className="nav-backdrop" aria-label="Close navigation" onClick={() => setMenuOpen(false)} />}
      <aside className={`panel-sidebar ${menuOpen ? 'is-open' : ''}`}>
        <Link to="/" className="brand" onClick={() => setMenuOpen(false)}>
          <span className="brand-mark"><PanelIcon name="activity" size={25} /></span>
          <span>FlowRate<span className="brand-subtitle">SERVER CONSOLE</span></span>
        </Link>
        <div className="nav-label">WORKSPACE</div>
        <nav aria-label="Main navigation" onClick={() => setMenuOpen(false)}>
          <NavLink to="/" end className="nav-link"><PanelIcon name="overview" />Statistics</NavLink>
          <NavLink to="/coordinators" className="nav-link"><PanelIcon name="server" />Coordinators<span className="nav-count">{overview.data?.coordinators.length ?? '—'}</span></NavLink>
          <NavLink to="/sfu-nodes" className="nav-link"><PanelIcon name="nodes" />SFU nodes</NavLink>
          {coordinatorId && <>
            <div className="nav-label coordinator-label">COORDINATOR</div>
            <NavLink to={`/coordinators/${coordinatorId}`} end className="nav-link"><PanelIcon name="activity" />Activity</NavLink>
            <NavLink to={`/coordinators/${coordinatorId}/roles`} className="nav-link"><PanelIcon name="shield" />Roles & access</NavLink>
            <NavLink to={`/coordinators/${coordinatorId}/settings`} className="nav-link"><PanelIcon name="settings" />Settings</NavLink>
          </>}
        </nav>
        <div className="sidebar-bottom">
          <div className="sidebar-version"><span className="status-dot" />FlowRate{overview.data && <span>v{overview.data.serverVersion}</span>}</div>
        </div>
      </aside>
      <div className="panel-workspace">
        <header className="panel-topbar">
          <Group gap="sm" wrap="nowrap">
            <ActionIcon className="mobile-menu" variant="subtle" color="gray" aria-label="Open navigation" onClick={() => setMenuOpen(true)}><PanelIcon name="menu" /></ActionIcon>
            <span className="breadcrumb-root">Workspace</span><span className="breadcrumb-slash">/</span>
            {coordinator && <><Link className="breadcrumb-coordinator" to={`/coordinators/${coordinatorId}`}>{coordinator.name}</Link><span className="breadcrumb-slash">/</span></>}
            <span className="breadcrumb-current">{section}</span>
          </Group>
          <Group gap="md" wrap="nowrap"><LiveBadge /><span className="topbar-divider" /><ColorSchemeToggle /></Group>
        </header>
        <main className="panel-content" id="main-content">
          <Routes>
            <Route index element={<OverviewPage />} />
            <Route path="coordinators" element={<CoordinatorsPage />} />
            <Route path="sfu-nodes" element={<SfuNodesPage />} />
            <Route path="coordinators/:id" element={<CoordinatorPage />} />
            <Route path="coordinators/:id/settings" element={<CoordinatorSettingsPage />} />
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
  open: { color: 'teal', label: 'Live updates', hint: 'Changes appear as they happen' },
  connecting: { color: 'gray', label: 'Connecting', hint: 'Opening the live updates stream' },
  reconnecting: { color: 'yellow', label: 'Reconnecting', hint: 'Live updates are interrupted; retrying' },
}

function LiveBadge() {
  const badge = liveBadges[useRealtimeStatus()]
  return <Tooltip label={badge.hint}><Badge color={badge.color} variant="dot" className="live-badge">{badge.label}</Badge></Tooltip>
}

function ColorSchemeToggle() {
  const { setColorScheme } = useMantineColorScheme()
  const scheme = useComputedColorScheme('light')
  const next = scheme === 'dark' ? 'light' : 'dark'
  return <Tooltip label={`Switch to ${next} theme`}><ActionIcon variant="subtle" color="gray" size="lg" aria-label={`Switch to ${next} theme`} onClick={() => setColorScheme(next)}><PanelIcon name={scheme === 'dark' ? 'sun' : 'moon'} /></ActionIcon></Tooltip>
}
