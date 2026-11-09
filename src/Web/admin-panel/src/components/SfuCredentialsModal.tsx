import { Alert, Button, Group, Modal, Stack, Switch, Text } from '@mantine/core'
import { useState, type SubmitEvent } from 'react'
import { ApiError, type SfuNode } from '../api/client'
import { useUpdateSfuCredentials } from '../api/hooks'
import { SfuCredentialsFields } from './SfuCredentialsFields'

export function SfuCredentialsModal({ node, onClose }: { node: SfuNode, onClose: () => void }) {
  const update = useUpdateSfuCredentials(node.id)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [clear, setClear] = useState(false)
  const errors = update.error instanceof ApiError ? update.error.errors : {}
  const busy = update.isPending

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault()
    update.mutate(clear ? { username: '', password: '' } : { username: username.trim(), password }, { onSuccess: onClose })
  }

  return <Modal opened onClose={onClose} title={`Credentials: ${node.name}`} closeOnClickOutside={!busy} closeOnEscape={!busy} withCloseButton={!busy}>
    <form onSubmit={submit}><Stack>
      <Text size="sm" c="dimmed">Enter the credentials configured on the SFU. They are not shown after saving. Changing them reconnects the node and can interrupt media.</Text>
      {update.error && <Alert color="red" title="Credentials were not saved">{update.error.message}</Alert>}
      <fieldset disabled={busy} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}><Stack>
        <Switch label="Connect without credentials" checked={clear} onChange={event => setClear(event.currentTarget.checked)} />
        {!clear && <SfuCredentialsFields username={username} password={password} onUsernameChange={setUsername} onPasswordChange={setPassword} errors={errors} isSecured={node.isSecured} />}
      </Stack></fieldset>
      <Group justify="flex-end">
        <Button variant="default" disabled={busy} onClick={onClose}>Cancel</Button>
        <Button type="submit" loading={busy} disabled={!clear && (!username.trim() || !password)}>Save credentials</Button>
      </Group>
    </Stack></form>
  </Modal>
}
