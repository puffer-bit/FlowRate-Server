import { Alert, Button, Group, Modal, MultiSelect, NumberInput, SimpleGrid, Stack, Text, TextInput } from '@mantine/core'
import { useState, type SubmitEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { useCreateCoordinator, useOverview, useSfuNodes } from '../api/hooks'
import { QueryState } from './QueryState'

export function CreateCoordinatorModal({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate()
  const overview = useOverview()
  const nodes = useSfuNodes()
  const create = useCreateCoordinator()
  const usedPorts = new Set(overview.data?.coordinators.map(item => item.port))
  let initialPort = 26666
  while (usedPorts.has(initialPort) && initialPort < 65535) initialPort++
  const [name, setName] = useState('')
  const [ip, setIp] = useState('0.0.0.0')
  const [port, setPort] = useState<number | string>(initialPort)
  const [maxOnlineUsers, setMaxOnlineUsers] = useState<number | string>(64)
  const [maxUsers, setMaxUsers] = useState<number | string>(256)
  const [nodeIds, setNodeIds] = useState<string[]>([])
  const errors = create.error instanceof ApiError ? create.error.errors : {}
  const error = (field: string) => errors[field]?.join(' ')
  const busy = create.isPending

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault()
    create.mutate({ name: name.trim(), ip: ip.trim(), port: Number(port),
      maxOnlineUsers: Number(maxOnlineUsers), maxUsers: Number(maxUsers), sfuNodeIds: nodeIds.map(Number) }, {
      onSuccess: result => {
        onClose()
        void navigate(`/coordinators/${encodeURIComponent(result.id)}`)
      },
    })
  }

  return (
    <Modal opened onClose={onClose} title="Create coordinator" size="lg" closeOnClickOutside={!busy}
      closeOnEscape={!busy} withCloseButton={!busy}>
      <form onSubmit={submit}>
        <Stack>
          <Text size="sm" c="dimmed">Create an independent coordinator on this server. It will start as soon as its settings are saved.</Text>
          {create.error && <Alert color="red" title="Coordinator was not created">{create.error.message}</Alert>}
          <fieldset disabled={busy} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
            <Stack>
              <TextInput label="Name" placeholder="e.g. Community" required maxLength={100} autoFocus
                value={name} onChange={event => setName(event.currentTarget.value)} error={error('Name')} />
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput label="Listen IP" description="0.0.0.0 listens on every IPv4 interface" required
                  value={ip} onChange={event => setIp(event.currentTarget.value)} error={error('Ip')} />
                <NumberInput label="Port" description="Each coordinator needs a free endpoint" required min={1} max={65535}
                  allowDecimal={false} value={port} onChange={setPort} error={error('Port')} />
                <NumberInput label="Max online users" required min={1} allowDecimal={false}
                  value={maxOnlineUsers} onChange={setMaxOnlineUsers} error={error('MaxOnlineUsers')} />
                <NumberInput label="Max registered users" required min={1} allowDecimal={false}
                  value={maxUsers} onChange={setMaxUsers} error={error('MaxUsers')} />
              </SimpleGrid>
              <QueryState query={nodes}>
                {items => <MultiSelect label="SFU nodes" description="Select media nodes for voice and video. Nodes can be shared between coordinators."
                  placeholder="Select media nodes" data={items.map(node => ({ value: String(node.id), label: `${node.name}${node.isEnabled ? '' : ' (disabled)'}`, disabled: !node.isEnabled }))}
                  value={nodeIds} onChange={setNodeIds} error={error('SfuNodeIds')} />}
              </QueryState>
              {nodeIds.length === 0 && <Alert color="yellow">Without SFU nodes, voice and video sessions cannot start. You can assign nodes later in settings.</Alert>}
              <Text size="xs" c="dimmed">Heartbeat, storage and rate limits use the server defaults. You can adjust them after creation.</Text>
            </Stack>
          </fieldset>
          <Group justify="flex-end">
            <Button variant="default" disabled={busy} onClick={onClose}>Cancel</Button>
            <Button type="submit" loading={busy} disabled={!name.trim() || !ip.trim() || !port || !maxOnlineUsers || !maxUsers}>Create coordinator</Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  )
}
