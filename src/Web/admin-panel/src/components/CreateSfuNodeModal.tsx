import { Alert, Button, Group, Modal, NumberInput, SimpleGrid, Stack, Switch, Text, TextInput } from '@mantine/core'
import { useState, type SubmitEvent } from 'react'
import { ApiError } from '../api/client'
import { useCreateSfuNode } from '../api/hooks'
import { SfuCredentialsFields } from './SfuCredentialsFields'

export function CreateSfuNodeModal({ onClose }: { onClose: () => void }) {
  const create = useCreateSfuNode()
  const [name, setName] = useState('')
  const [host, setHost] = useState('')
  const [port, setPort] = useState<number | string>(50051)
  const [isSecured, setIsSecured] = useState(false)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const busy = create.isPending
  const errors = create.error instanceof ApiError ? create.error.errors : {}
  const error = (field: string) => errors[field]?.join(' ') ?? errors[field.toLowerCase()]?.join(' ')

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault()
    create.mutate({ name: name.trim(), host: host.trim(), port: Number(port), isSecured, username: username.trim(), password }, { onSuccess: onClose })
  }

  return (
    <Modal opened onClose={onClose} title="Add SFU node" closeOnClickOutside={!busy} closeOnEscape={!busy} withCloseButton={!busy}>
      <form onSubmit={submit}>
        <Stack>
          <Text size="sm" c="dimmed">Connect an existing SFU service to the global pool. Assign it to coordinators in their settings to use it for voice and video.</Text>
          {create.error && <Alert color="red" title="SFU node was not added">{create.error.message}</Alert>}
          <fieldset disabled={busy} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
            <Stack>
              <TextInput label="Name" placeholder="e.g. Media node 2" required maxLength={100} autoFocus value={name}
                onChange={event => setName(event.currentTarget.value)} error={error('Name')} />
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput label="Host" description="Hostname or IPv4 address, without a scheme" placeholder="sfu.example.com" required maxLength={253}
                  value={host} onChange={event => setHost(event.currentTarget.value)} error={error('Host')} />
                <NumberInput label="Port" required min={1} max={65535} allowDecimal={false} value={port} onChange={setPort} error={error('Port')} />
              </SimpleGrid>
              <Switch label="Use TLS" description="Connect to the SFU over HTTPS" checked={isSecured} onChange={event => setIsSecured(event.currentTarget.checked)} />
              <SfuCredentialsFields username={username} password={password} onUsernameChange={setUsername} onPasswordChange={setPassword} errors={errors} isSecured={isSecured} />
            </Stack>
          </fieldset>
          <Group justify="flex-end">
            <Button variant="default" disabled={busy} onClick={onClose}>Cancel</Button>
            <Button type="submit" loading={busy} disabled={!name.trim() || !host.trim() || !port}>Add node</Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  )
}
