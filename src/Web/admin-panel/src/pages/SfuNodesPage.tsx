import { Alert, Badge, Button, Group, Stack, Table, Text, Title } from '@mantine/core'
import { useState } from 'react'
import { CreateSfuNodeModal } from '../components/CreateSfuNodeModal'
import { SfuCredentialsModal } from '../components/SfuCredentialsModal'
import type { SfuNode } from '../api/client'
import { useNow, useSfuNodes } from '../api/hooks'
import { formatSince } from '../format'
import { PanelIcon } from '../components/PanelIcon'
import { QueryState } from '../components/QueryState'
import { SfuNodeStatusBadge } from '../components/SfuNodeStatusBadge'
import { NodeAddress, NodeDuration, NodeIdentity, TableLabel, TableMetric } from '../components/InfrastructureCells'

export function SfuNodesPage() {
  const sfuNodes = useSfuNodes()
  const now = useNow()
  const [creating, setCreating] = useState(false)
  const [credentialsNode, setCredentialsNode] = useState<SfuNode | null>(null)

  return (
    <Stack gap="xl">
      <div className="page-heading">
        <div>
          <div className="eyebrow">MEDIA INFRASTRUCTURE</div>
          <Title order={1}>SFU nodes<span className="heading-dot">.</span></Title>
          <Text c="dimmed" mt={8}>Monitor the media nodes powering voice and video sessions.</Text>
        </div>
        <Group>
          <Button variant="default" leftSection={<PanelIcon name="refresh" size={16} />} loading={sfuNodes.isFetching} onClick={() => { void sfuNodes.refetch() }}>Refresh</Button>
          <Button onClick={() => setCreating(true)}>Add SFU node</Button>
        </Group>
      </div>
    <section className="data-section" aria-label="SFU nodes list">
      <div className="section-heading"><div><Group gap="sm"><span className="section-icon"><PanelIcon name="nodes" /></span><Title order={3}>SFU nodes</Title>{sfuNodes.data && <Badge color="gray" variant="light">{sfuNodes.data.length}</Badge>}</Group><Text size="sm" c="dimmed" mt={6}>The media layer behind your voice and video sessions.</Text></div><span className="section-meta">Checked every 5 seconds</span></div>
      <QueryState query={sfuNodes}>
        {nodes => nodes.length === 0
          ? <div className="empty-state"><PanelIcon name="nodes" size={30} /><Text fw={600}>No media nodes connected</Text><Text size="sm" c="dimmed">Add an SFU node to the orchestrator to enable voice sessions.</Text></div>
          : (
            <Stack gap="sm">
              <SfuNodesAlert nodes={nodes} />
              <Table.ScrollContainer minWidth={760}>
                <Table highlightOnHover className="infrastructure-table">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th><TableLabel icon="nodes">Name</TableLabel></Table.Th>
                      <Table.Th><TableLabel icon="network">Address</TableLabel></Table.Th>
                      <Table.Th><TableLabel icon="activity">State</TableLabel></Table.Th>
                      <Table.Th><TableLabel icon="clock">In this state</TableLabel></Table.Th>
                      <Table.Th ta="right"><TableLabel icon="sessions">Sessions</TableLabel></Table.Th>
                      <Table.Th ta="right"><TableLabel icon="cpu">Workers</TableLabel></Table.Th>
                      <Table.Th>Credentials</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {nodes.map(node => (
                      <Table.Tr key={node.id}>
                        <Table.Td><NodeIdentity name={node.name} icon="nodes" state={!node.isEnabled ? 'Disabled' : node.status?.state ?? 'Connecting'} /></Table.Td>
                        <Table.Td><NodeAddress host={node.host} port={node.port} secured={node.isSecured} /></Table.Td>
                        <Table.Td><SfuNodeStatusBadge node={node} /></Table.Td>
                        <Table.Td><NodeDuration value={node.status ? formatSince(node.status.stateChangedAtUtc, now) : '—'} /></Table.Td>
                        <Table.Td ta="right"><TableMetric value={node.status?.sessionCount} /></Table.Td>
                        <Table.Td ta="right"><TableMetric value={node.status?.workerCount} /></Table.Td>
                        <Table.Td><Button variant="subtle" size="xs" aria-label={`Edit credentials for ${node.name}`} onClick={() => setCredentialsNode(node)}>Edit</Button></Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            </Stack>
          )}
      </QueryState>
    </section>
    {creating && <CreateSfuNodeModal onClose={() => setCreating(false)} />}
    {credentialsNode && <SfuCredentialsModal node={credentialsNode} onClose={() => setCredentialsNode(null)} />}
    </Stack>
  )
}

/** Enabled nodes that cannot take sessions right now. */
const isSfuNodeDown = (node: SfuNode) => node.isEnabled && node.status != null && node.status.state !== 'Connected'

function SfuNodesAlert({ nodes }: { nodes: SfuNode[] }) {
  const down = nodes.filter(isSfuNodeDown)
  if (down.length === 0)
    return null

  const allDown = nodes.filter(node => node.isEnabled).length === down.length
  return (
    <Alert color={allDown ? 'red' : 'orange'}
      title={allDown ? 'No SFU node is connected' : `${down.length} SFU node${down.length > 1 ? 's are' : ' is'} not connected`}>
      {allDown
        ? 'Audio and video are unavailable until a node reconnects. Participants can still join sessions.'
        : `${down.map(node => node.name).join(', ')}: unavailable. Sessions use another assigned node when available.`}
    </Alert>
  )
}
