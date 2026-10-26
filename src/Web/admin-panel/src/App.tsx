import { ActionIcon, AppShell, Badge, Container, Group, Title, Tooltip, useComputedColorScheme, useMantineColorScheme } from '@mantine/core'
import { Link, Navigate, Route, Routes } from 'react-router'
import { useLiveUpdates, useRealtimeStatus } from './api/hooks'
import type { RealtimeStatus } from './api/realtime'
import { CoordinatorPage } from './pages/CoordinatorPage'
import { CoordinatorSettingsPage } from './pages/CoordinatorSettingsPage'
import { OverviewPage } from './pages/OverviewPage'
import { RolesPage } from './pages/RolesPage'

export function App() {
  useLiveUpdates()

  return (
    <AppShell header={{ height: 56 }} padding="md">
      <AppShell.Header>
        <Container size="lg" h="100%">
          <Group h="100%" justify="space-between">
            <Link to="/" style={{ color: 'inherit', textDecoration: 'none' }}>
              <Title order={4}>FlowRate Panel</Title>
            </Link>
            <Group gap="xs">
              <LiveBadge />
              <ColorSchemeToggle />
            </Group>
          </Group>
        </Container>
      </AppShell.Header>

      <AppShell.Main>
        <Container size="lg">
          <Routes>
            <Route index element={<OverviewPage />} />
            <Route path="coordinators/:id" element={<CoordinatorPage />} />
            <Route path="coordinators/:id/settings" element={<CoordinatorSettingsPage />} />
            <Route path="coordinators/:id/roles" element={<RolesPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Container>
      </AppShell.Main>
    </AppShell>
  )
}

const liveBadges: Record<RealtimeStatus, { color: string, label: string, hint: string }> = {
  open: { color: 'green', label: 'Live', hint: 'Changes appear as they happen' },
  connecting: { color: 'gray', label: 'Connecting', hint: 'Opening the live updates stream' },
  reconnecting: { color: 'yellow', label: 'Reconnecting', hint: 'Live updates are interrupted; retrying' },
}

function LiveBadge() {
  const badge = liveBadges[useRealtimeStatus()]
  return (
    <Tooltip label={badge.hint}>
      <Badge color={badge.color} variant="dot">{badge.label}</Badge>
    </Tooltip>
  )
}

function ColorSchemeToggle() {
  const { setColorScheme } = useMantineColorScheme()
  const scheme = useComputedColorScheme('light')
  const next = scheme === 'dark' ? 'light' : 'dark'

  return (
    <ActionIcon variant="default" aria-label={`Switch to ${next} theme`} onClick={() => setColorScheme(next)}>
      {scheme === 'dark' ? '☀' : '☾'}
    </ActionIcon>
  )
}
