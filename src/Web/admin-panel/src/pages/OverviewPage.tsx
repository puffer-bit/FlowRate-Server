import { Button, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { useNow, useOverview } from '../api/hooks'
import { formatSince } from '../format'
import { PanelIcon } from '../components/PanelIcon'
import { QueryState } from '../components/QueryState'
import { StatCard } from '../components/StatCard'

export function OverviewPage() {
  const overview = useOverview()
  const now = useNow()

  return (
    <QueryState query={overview}>
      {data => (
        <Stack gap="xl">
          <div className="page-heading">
            <div><div className="eyebrow">YOUR SERVER AT A GLANCE</div><Title order={1}>Server statistics<span className="heading-dot">.</span></Title><Text c="dimmed" mt={8}>Live totals across all coordinators and the server uptime.</Text></div>
            <Button variant="default" leftSection={<PanelIcon name="refresh" size={16} />} loading={overview.isFetching} onClick={() => { void overview.refetch() }}>Refresh</Button>
          </div>
          <SimpleGrid cols={{ base: 2, sm: 4 }}>
            <StatCard label="Coordinators" value={data.coordinators.length} icon="server" hint={`${data.coordinators.filter(item => item.state === 'Online').length} online now`} />
            <StatCard label="Active sessions" value={data.activeSessionsCount} icon="activity" hint="Across all coordinators" />
            <StatCard label="Connected users" value={data.connectedUsersCount} icon="users" hint="Currently connected" />
            <StatCard label="Server uptime" value={formatSince(data.startedAtUtc, now)} icon="clock" hint="Since the last server start" />
          </SimpleGrid>

          <div className="overview-note"><PanelIcon name="activity" size={16} /><span>Metrics update automatically as your infrastructure changes.</span><span className="version-tag">Server v{data.serverVersion}</span></div>
        </Stack>
      )}
    </QueryState>
  )
}
