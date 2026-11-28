import { CreateCoordinatorModal } from '../components/CreateCoordinatorModal'
import { useState } from 'react'
import { ActionIcon, Badge, Button, Group, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { Link } from 'react-router'
import { useNow, useOverview } from '../api/hooks'
import { formatSince } from '../format'
import { PanelIcon } from '../components/PanelIcon'
import { QueryState } from '../components/QueryState'
import { StateBadge } from '../components/StateBadge'
import { NodeAddress, NodeDuration, NodeIdentity, TableLabel, TableMetric } from '../components/InfrastructureCells'

export function CoordinatorsPage() {
  const overview = useOverview()
  const now = useNow()
  const [search, setSearch] = useState('')
  const [creating, setCreating] = useState(false)

  return (
    <Stack gap="xl">
      <div className="page-heading">
        <div>
          <div className="eyebrow">SERVER INFRASTRUCTURE</div>
          <Title order={1}>Coordinators<span className="heading-dot">.</span></Title>
          <Text c="dimmed" mt={8}>Browse all coordinators and open one to manage its sessions, users and settings.</Text>
        </div>
        <Group gap="sm">
          <Button variant="default" leftSection={<PanelIcon name="refresh" size={16} />} loading={overview.isFetching} onClick={() => { void overview.refetch() }}>Refresh</Button>
          <Button onClick={() => setCreating(true)}>Create coordinator</Button>
        </Group>
      </div>
      <QueryState query={overview}>
        {data => (
          <section className="data-section" aria-label="Coordinators list">
          <div className="section-heading"><div><Group gap="sm"><span className="section-icon"><PanelIcon name="server" /></span><Title order={3}>Coordinators</Title><Badge variant="light" color="gray">{data.coordinators.length}</Badge></Group><Text size="sm" c="dimmed" mt={6}>Manage your servers and their active connections.</Text></div><TextInput aria-label="Search coordinators" placeholder="Find a coordinator…" leftSection={<PanelIcon name="search" size={16} />} value={search} onChange={event => setSearch(event.currentTarget.value)} /></div>
          {data.coordinators.length === 0
            ? <div className="empty-state"><PanelIcon name="server" size={30} /><Text fw={600}>No coordinators yet</Text><Text size="sm" c="dimmed">Create a coordinator to start accepting connections.</Text></div>
            : (
              <Table.ScrollContainer minWidth={760}>
                <Table highlightOnHover className="infrastructure-table">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th><TableLabel icon="server">Name</TableLabel></Table.Th>
                      <Table.Th><TableLabel icon="network">Address</TableLabel></Table.Th>
                      <Table.Th><TableLabel icon="activity">State</TableLabel></Table.Th>
                      <Table.Th><TableLabel icon="clock">Uptime</TableLabel></Table.Th>
                      <Table.Th ta="right"><TableLabel icon="sessions">Sessions</TableLabel></Table.Th>
                      <Table.Th ta="right"><TableLabel icon="activity">Active</TableLabel></Table.Th>
                      <Table.Th ta="right"><TableLabel icon="users">Users</TableLabel></Table.Th>
                      <Table.Th aria-label="Actions" w={40} />
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {data.coordinators.filter(item => `${item.name} ${item.ip}:${item.port}`.toLowerCase().includes(search.toLowerCase())).map(coordinator => (
                      <Table.Tr key={coordinator.id}>
                        <Table.Td>
                          <NodeIdentity name={coordinator.name} icon="server" state={coordinator.state} to={`/coordinators/${coordinator.id}`} />
                        </Table.Td>
                        <Table.Td><NodeAddress host={coordinator.ip} port={coordinator.port} /></Table.Td>
                        <Table.Td><StateBadge state={coordinator.state} /></Table.Td>
                        <Table.Td><NodeDuration value={coordinator.state === 'Online' ? formatSince(coordinator.startedAtUtc, now) : '—'} /></Table.Td>
                        <Table.Td ta="right"><TableMetric value={coordinator.sessionsCount} /></Table.Td>
                        <Table.Td ta="right"><TableMetric value={coordinator.activeSessionsCount} /></Table.Td>
                        <Table.Td ta="right"><TableMetric value={coordinator.connectedUsersCount} /></Table.Td>
                        <Table.Td><ActionIcon component={Link} to={`/coordinators/${coordinator.id}`} variant="subtle" color="gray" size="sm" aria-label={`Open ${coordinator.name}`}><PanelIcon name="arrow" size={15} /></ActionIcon></Table.Td>
                      </Table.Tr>
                    ))}
                    {data.coordinators.length > 0 && !data.coordinators.some(item => `${item.name} ${item.ip}:${item.port}`.toLowerCase().includes(search.toLowerCase())) && <Table.Tr><Table.Td colSpan={8}><Text ta="center" c="dimmed" py="xl">No coordinators match “{search}”.</Text></Table.Td></Table.Tr>}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            )}

          </section>
        )}
      </QueryState>
      {creating && <CreateCoordinatorModal onClose={() => setCreating(false)} />}
    </Stack>
  )
}
