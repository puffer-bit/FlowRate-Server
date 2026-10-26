import {
  Alert,
  Anchor,
  Badge,
  Button,
  Checkbox,
  Fieldset,
  Group,
  Modal,
  NumberInput,
  PasswordInput,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  TagsInput,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import {
  ApiError,
  isConflict,
  isNotFound,
  type CoordinatorSettings,
  type RateLimitSettings,
  type SfuNode,
  type UpdateCoordinatorSettings,
} from '../api/client'
import { useCoordinator, useCoordinatorSettings, useSfuNodes, useUpdateCoordinatorSettings } from '../api/hooks'
import { SfuNodeStatusBadge } from '../components/SfuNodeStatusBadge'
import { QueryState } from '../components/QueryState'

const megabyte = 1024 * 1024

type PasswordMode = 'keep' | 'set' | 'remove'

/** The request groups of the rate limits, in the order they are shown. */
const rateGroups = [
  { key: 'global', label: 'All requests', description: 'Every request of one connection also counts here' },
  { key: 'authenticationPerIp', label: 'Logins', description: 'Per IP address, over all its connections' },
  { key: 'sessionManagement', label: 'Session management', description: 'Creating, deleting and configuring sessions' },
  { key: 'sessionLayout', label: 'Session layout', description: 'Categories and the order of sessions' },
  { key: 'sessionMedia', label: 'Session media', description: 'Joining, voice, video and screen share' },
  { key: 'microphoneMute', label: 'Mute', description: 'Muting the microphone or the sound' },
  { key: 'inboundResume', label: 'Inbound resume', description: 'Resuming received streams after a reconnect' },
  { key: 'moderation', label: 'Moderation', description: 'Approving, kicking and banning users, roles' },
  { key: 'chatWrite', label: 'Chat writes', description: 'Sending, editing and deleting messages, push replies' },
  { key: 'chatRead', label: 'Chat reads', description: 'History and read marks' },
  { key: 'pushRegistration', label: 'Push registration', description: 'Registering devices for notifications' },
  { key: 'mediaToken', label: 'Media tokens', description: 'Tokens for uploading and downloading files' },
  { key: 'query', label: 'Other requests', description: 'Every request of no other group' },
  { key: 'heartbeat', label: 'Heartbeat', description: 'Keep-alive pings of the client' },
] as const

type RateGroup = (typeof rateGroups)[number]['key']

interface RateForm {
  count: number | string
  periodSeconds: number | string
}

/** What the form edits: the settings with every field present, and media sizes in megabytes. */
interface Form {
  name: string
  ip: string
  port: number | string
  passwordMode: PasswordMode
  newPassword: string
  maxOnlineUsers: number | string
  maxUsers: number | string
  motdEnabled: boolean
  motdText: string
  allowedIps: string[]
  bannedIps: string[]
  heartbeatInterval: number | string
  heartbeatTimeout: number | string
  maxFileSizeMb: number | string
  storageQuotaMb: number | string
  maxUnattachedUploads: number | string
  unattachedLifetimeHours: number | string
  tokenLifetimeMinutes: number | string
  rates: Record<RateGroup, RateForm>
  maxRejectionsPerWindow: number | string
  rejectionWindowSeconds: number | string
  maxConnectionsPerIp: number | string
  sfuNodeIds: string[]
}

function toForm(settings: CoordinatorSettings): Form {
  return {
    name: settings.name,
    ip: settings.ip,
    port: settings.port,
    passwordMode: 'keep',
    newPassword: '',
    maxOnlineUsers: settings.maxOnlineUsers,
    maxUsers: settings.maxUsers,
    motdEnabled: settings.motd.isEnabled ?? false,
    motdText: settings.motd.text ?? '',
    allowedIps: settings.accessList.allowedIps ?? [],
    bannedIps: settings.accessList.bannedIps ?? [],
    heartbeatInterval: settings.heartbeat.intervalSeconds ?? '',
    heartbeatTimeout: settings.heartbeat.timeoutSeconds ?? '',
    maxFileSizeMb: Math.round((settings.media.maxFileSizeBytes ?? 0) / megabyte),
    storageQuotaMb: Math.round((settings.media.storageQuotaBytes ?? 0) / megabyte),
    maxUnattachedUploads: settings.uploads.maxUnattachedPerUser ?? '',
    unattachedLifetimeHours: settings.uploads.unattachedLifetimeHours ?? '',
    tokenLifetimeMinutes: settings.uploads.tokenLifetimeMinutes ?? '',
    rates: Object.fromEntries(rateGroups.map(({ key }) => [key, {
      count: settings.rateLimits[key].count ?? '',
      periodSeconds: settings.rateLimits[key].periodSeconds ?? '',
    }])) as Record<RateGroup, RateForm>,
    maxRejectionsPerWindow: settings.rateLimits.maxRejectionsPerWindow ?? '',
    rejectionWindowSeconds: settings.rateLimits.rejectionWindowSeconds ?? '',
    maxConnectionsPerIp: settings.rateLimits.maxConnectionsPerIp ?? '',
    sfuNodeIds: settings.sfuNodeIds.map(String),
  }
}

// Empty number inputs are sent as 0 and rejected by the server with a message for the field
const toNumber = (value: number | string) => (typeof value === 'number' ? value : Number(value) || 0)
const toOptionalText = (value: string) => (value.trim() === '' ? null : value.trim())

function toUpdate(form: Form, expectedRevision: number): UpdateCoordinatorSettings {
  return {
    expectedRevision,
    name: form.name.trim(),
    ip: form.ip.trim(),
    port: toNumber(form.port),
    newPassword: form.passwordMode === 'set' ? form.newPassword : null,
    removePassword: form.passwordMode === 'remove',
    maxOnlineUsers: toNumber(form.maxOnlineUsers),
    maxUsers: toNumber(form.maxUsers),
    motd: { isEnabled: form.motdEnabled, text: toOptionalText(form.motdText) },
    accessList: { allowedIps: form.allowedIps, bannedIps: form.bannedIps },
    heartbeat: { intervalSeconds: toNumber(form.heartbeatInterval), timeoutSeconds: toNumber(form.heartbeatTimeout) },
    media: {
      maxFileSizeBytes: toNumber(form.maxFileSizeMb) * megabyte,
      storageQuotaBytes: toNumber(form.storageQuotaMb) * megabyte,
    },
    uploads: {
      maxUnattachedPerUser: toNumber(form.maxUnattachedUploads),
      unattachedLifetimeHours: toNumber(form.unattachedLifetimeHours),
      tokenLifetimeMinutes: toNumber(form.tokenLifetimeMinutes),
    },
    rateLimits: {
      ...Object.fromEntries(rateGroups.map(({ key }) => [key, {
        count: toNumber(form.rates[key].count),
        periodSeconds: toNumber(form.rates[key].periodSeconds),
      }])) as Pick<RateLimitSettings, RateGroup>,
      maxRejectionsPerWindow: toNumber(form.maxRejectionsPerWindow),
      rejectionWindowSeconds: toNumber(form.rejectionWindowSeconds),
      maxConnectionsPerIp: toNumber(form.maxConnectionsPerIp),
    },
    sfuNodeIds: form.sfuNodeIds.map(Number),
  }
}

export function CoordinatorSettingsPage() {
  const { id = '' } = useParams()
  const settings = useCoordinatorSettings(id)

  if (isNotFound(settings.error))
    return <Alert color="yellow" title="Coordinator not found">There is no coordinator with this id.</Alert>

  return (
    <QueryState query={settings}>
      {/* Keyed by the coordinator only: newer settings reach a form being edited without wiping it */}
      {data => <SettingsForm key={id} id={id} saved={data} />}
    </QueryState>
  )
}

/**
 * Edits the settings it started from, `base`. Newer settings saved elsewhere (another panel, a client) replace an
 * untouched form at once; a form with edits keeps them and offers to load the newer values instead.
 */
function SettingsForm({ id, saved: latest }: { id: string, saved: CoordinatorSettings }) {
  const [base, setBase] = useState(latest)
  const [form, setForm] = useState(() => toForm(latest))
  const [justSaved, setJustSaved] = useState(false)
  const [confirmingRestart, setConfirmingRestart] = useState(false)
  const update = useUpdateCoordinatorSettings(id)
  const sfuNodes = useSfuNodes()
  const onlineCount = useCoordinator(id).data?.summary.connectedUsersCount

  const set = <K extends keyof Form>(key: K) => (value: Form[K]) => {
    setForm(current => ({ ...current, [key]: value }))
    setJustSaved(false)
  }
  const setRate = (group: RateGroup, part: keyof RateForm) => (value: number | string) => {
    setForm(current => ({ ...current, rates: { ...current.rates, [group]: { ...current.rates[group], [part]: value } } }))
    setJustSaved(false)
  }

  const initial = toForm(base)
  const isDirty = JSON.stringify(form) !== JSON.stringify(initial)
  const changedElsewhere = latest.revision !== base.revision
  const startOver = (settings: CoordinatorSettings) => {
    setBase(settings)
    setForm(toForm(settings))
  }
  if (changedElsewhere && !isDirty)
    startOver(latest)

  const saved = base
  const movesEndpoint = form.ip.trim() !== saved.ip || toNumber(form.port) !== saved.port
  const errors = update.error instanceof ApiError ? update.error.errors : {}
  const fieldErrors = new FieldErrors(errors)
  // Taken here: the picker renders inside a query state, after the leftover errors are collected
  const sfuNodesError = fieldErrors.take('SfuNodeIds')

  const save = () => {
    update.mutate(toUpdate(form, base.revision), {
      onSuccess: settings => {
        startOver(settings)
        setJustSaved(true)
      },
    })
  }

  const loadLatest = () => {
    startOver(latest)
    update.reset()
  }

  return (
    <Stack>
      <div>
        <Anchor component={Link} to={`/coordinators/${encodeURIComponent(id)}`} size="sm">← {saved.name}</Anchor>
        <Group gap="xs" align="baseline">
          <Title order={2}>Settings</Title>
          {saved.isDefault && <Badge variant="light">default coordinator</Badge>}
        </Group>
        <Text size="sm" c="dimmed">
          Saved settings take effect at once, without restarting the coordinator. Connected users get the new values.
          Users can change the same settings from the client; this page follows their changes.
        </Text>
      </div>

      {changedElsewhere && (
        <Alert color="yellow" title="The settings were changed elsewhere">
          <Stack gap="xs" align="flex-start">
            <span>
              Someone saved other settings while you were editing, from another panel or from a client. Your edits
              cannot be saved over them: load the new values and make your change again.
            </span>
            <Button size="xs" variant="light" color="yellow" onClick={loadLatest}>
              Load the new values, drop my edits
            </Button>
          </Stack>
        </Alert>
      )}

      <Fieldset legend="General">
        <Stack>
          <TextInput label="Name" required value={form.name} onChange={e => set('name')(e.currentTarget.value)}
            error={fieldErrors.take('Name')} />
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <TextInput label="Listen IP" description="0.0.0.0 listens on every interface" required value={form.ip}
              onChange={e => set('ip')(e.currentTarget.value)} error={fieldErrors.take('Ip')} />
            <NumberInput label="Port" required min={1} max={65535} allowDecimal={false} value={form.port}
              onChange={set('port')} error={fieldErrors.take('Port')} />
          </SimpleGrid>
          {movesEndpoint && (
            <Alert color="yellow" title="The coordinator will restart">
              A new address or port needs a new listener: saving disconnects every user, and they have to connect to
              the new address.
            </Alert>
          )}
          <Textarea label="Message of the day" autosize minRows={2} value={form.motdText}
            onChange={e => set('motdText')(e.currentTarget.value)} disabled={!form.motdEnabled}
            error={fieldErrors.take('Motd.Text')} />
          <Switch label="Show the message of the day" checked={form.motdEnabled}
            onChange={e => set('motdEnabled')(e.currentTarget.checked)} />
        </Stack>
      </Fieldset>

      <Fieldset legend="Users">
        <Stack>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <NumberInput label="Max online users" description="Further logins are refused while this many are online"
              required min={1} allowDecimal={false} thousandSeparator=" " value={form.maxOnlineUsers}
              onChange={set('maxOnlineUsers')} error={fieldErrors.take('MaxOnlineUsers')} />
            <NumberInput label="Max registered users" description="New users are refused once this many are registered"
              required min={1} allowDecimal={false} thousandSeparator=" " value={form.maxUsers}
              onChange={set('maxUsers')} error={fieldErrors.take('MaxUsers')} />
          </SimpleGrid>
          {onlineCount !== undefined && onlineCount > toNumber(form.maxOnlineUsers) && (
            <Alert color="yellow">
              {onlineCount} users are online now. They stay connected; new logins wait until fewer are online.
            </Alert>
          )}
        </Stack>
      </Fieldset>

      <Fieldset legend="Access">
        <Stack>
          <div>
            <Group gap="xs" mb={4}>
              <Text size="sm" fw={500}>Server password</Text>
              <Badge size="sm" variant="light" color={saved.isPasswordProtected ? 'blue' : 'gray'}>
                {saved.isPasswordProtected ? 'set' : 'not set'}
              </Badge>
            </Group>
            <SegmentedControl value={form.passwordMode} onChange={value => set('passwordMode')(value as PasswordMode)}
              data={[
                { value: 'keep', label: 'Keep' },
                { value: 'set', label: saved.isPasswordProtected ? 'Change' : 'Set' },
                { value: 'remove', label: 'Remove', disabled: !saved.isPasswordProtected },
              ]} />
          </div>
          {form.passwordMode === 'set' && (
            <PasswordInput label="New password" description="Users already online are not asked for it" required
              value={form.newPassword} onChange={e => set('newPassword')(e.currentTarget.value)}
              error={fieldErrors.take('NewPassword')} />
          )}
          <TagsInput label="Allowed IPs" description="When not empty, only these addresses may connect"
            placeholder="Type an IP and press Enter" value={form.allowedIps} onChange={set('allowedIps')}
            error={fieldErrors.take('AccessList.AllowedIps')} />
          <TagsInput label="Banned IPs" placeholder="Type an IP and press Enter" value={form.bannedIps}
            onChange={set('bannedIps')} error={fieldErrors.take('AccessList.BannedIps')} />
        </Stack>
      </Fieldset>

      <Fieldset legend="Heartbeat">
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <NumberInput label="Check interval, s" required min={1} max={3600} allowDecimal={false}
            value={form.heartbeatInterval} onChange={set('heartbeatInterval')}
            error={fieldErrors.take('Heartbeat.IntervalSeconds')} />
          <NumberInput label="Timeout, s" description="A client silent for longer is disconnected" required min={1}
            max={3600} allowDecimal={false} value={form.heartbeatTimeout} onChange={set('heartbeatTimeout')}
            error={fieldErrors.take('Heartbeat.TimeoutSeconds')} />
        </SimpleGrid>
      </Fieldset>

      <Fieldset legend="Media">
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <NumberInput label="Max file size, MB" required min={1} allowDecimal={false} thousandSeparator=" "
            value={form.maxFileSizeMb} onChange={set('maxFileSizeMb')}
            error={fieldErrors.take('Media.MaxFileSizeBytes')} />
          <NumberInput label="Storage quota, MB" description="0 leaves the storage unlimited" required min={0}
            allowDecimal={false} thousandSeparator=" " value={form.storageQuotaMb} onChange={set('storageQuotaMb')}
            error={fieldErrors.take('Media.StorageQuotaBytes')} />
        </SimpleGrid>
      </Fieldset>

      <Fieldset legend="Uploads">
        <SimpleGrid cols={{ base: 1, sm: 3 }}>
          <NumberInput label="Unattached uploads per user"
            description="Files uploaded but not sent in a message yet" required min={1} max={1000}
            allowDecimal={false} value={form.maxUnattachedUploads} onChange={set('maxUnattachedUploads')}
            error={fieldErrors.take('Uploads.MaxUnattachedPerUser')} />
          <NumberInput label="Unattached upload lifetime, h" description="Unsent files are deleted after this"
            required min={1} max={720} allowDecimal={false} value={form.unattachedLifetimeHours}
            onChange={set('unattachedLifetimeHours')} error={fieldErrors.take('Uploads.UnattachedLifetimeHours')} />
          <NumberInput label="Media token lifetime, min" description="Issued tokens keep their old expiry"
            required min={1} max={1440} allowDecimal={false} value={form.tokenLifetimeMinutes}
            onChange={set('tokenLifetimeMinutes')} error={fieldErrors.take('Uploads.TokenLifetimeMinutes')} />
        </SimpleGrid>
      </Fieldset>

      <Fieldset legend="Rate limits">
        <Stack>
          <Text size="sm" c="dimmed">
            At most this many requests of a group per period, for each connection. A request over the limit is refused;
            new limits start every connection with a full budget.
          </Text>
          <Table.ScrollContainer minWidth={560}>
            <Table verticalSpacing={4}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Requests</Table.Th>
                  <Table.Th w={140}>Count</Table.Th>
                  <Table.Th w={140}>Period, s</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {rateGroups.map(({ key, label, description }) => {
                  const field = `RateLimits.${key}`
                  return (
                    <Table.Tr key={key}>
                      <Table.Td>
                        <Text size="sm">{label}</Text>
                        <Text size="xs" c="dimmed">{description}</Text>
                      </Table.Td>
                      <Table.Td>
                        <NumberInput aria-label={`${label}: count`} size="xs" required min={1} allowDecimal={false}
                          thousandSeparator=" " value={form.rates[key].count} onChange={setRate(key, 'count')}
                          error={fieldErrors.take(`${field}.Count`)} />
                      </Table.Td>
                      <Table.Td>
                        <NumberInput aria-label={`${label}: period`} size="xs" required min={0.001} decimalScale={3}
                          value={form.rates[key].periodSeconds} onChange={setRate(key, 'periodSeconds')}
                          error={fieldErrors.take(`${field}.PeriodSeconds`)} />
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <NumberInput label="Refusals before closing" description="A connection refused this often is closed"
              required min={1} allowDecimal={false} value={form.maxRejectionsPerWindow}
              onChange={set('maxRejectionsPerWindow')} error={fieldErrors.take('RateLimits.MaxRejectionsPerWindow')} />
            <NumberInput label="Refusal window, s" required min={1} max={86400} allowDecimal={false}
              value={form.rejectionWindowSeconds} onChange={set('rejectionWindowSeconds')}
              error={fieldErrors.take('RateLimits.RejectionWindowSeconds')} />
            <NumberInput label="Connections per IP"
              description="Raise it when clients come through a proxy or a shared address" required min={1}
              allowDecimal={false} value={form.maxConnectionsPerIp} onChange={set('maxConnectionsPerIp')}
              error={fieldErrors.take('RateLimits.MaxConnectionsPerIp')} />
          </SimpleGrid>
        </Stack>
      </Fieldset>

      <Fieldset legend="SFU nodes">
        <QueryState query={sfuNodes}>
          {nodes => (
            <SfuNodesPicker nodes={nodes} value={form.sfuNodeIds} onChange={set('sfuNodeIds')}
              error={sfuNodesError} />
          )}
        </QueryState>
      </Fieldset>

      <SaveStatus error={update.error} otherErrors={fieldErrors.rest()} justSaved={justSaved && !isDirty} />

      <RestartConfirmation opened={confirmingRestart} onClose={() => setConfirmingRestart(false)}
        onConfirm={() => {
          setConfirmingRestart(false)
          save()
        }} />

      <Group justify="flex-end">
        <Button variant="default" disabled={!isDirty || update.isPending} onClick={() => setForm(initial)}>
          Reset
        </Button>
        <Button disabled={!isDirty || changedElsewhere} loading={update.isPending}
          onClick={movesEndpoint ? () => setConfirmingRestart(true) : save}>
          {movesEndpoint ? 'Save and restart' : 'Save'}
        </Button>
      </Group>
    </Stack>
  )
}

function RestartConfirmation({ opened, onClose, onConfirm }: {
  opened: boolean
  onClose: () => void
  onConfirm: () => void
}) {
  return (
    <Modal opened={opened} onClose={onClose} title="Restart the coordinator?" centered>
      <Stack>
        <Text size="sm">
          The coordinator restarts on the new address or port. Every user is disconnected and has to connect to the
          new address.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>Cancel</Button>
          <Button color="yellow" onClick={onConfirm}>Save and restart</Button>
        </Group>
      </Stack>
    </Modal>
  )
}

function SfuNodesPicker({ nodes, value, onChange, error }: {
  nodes: SfuNode[]
  value: string[]
  onChange: (value: string[]) => void
  error?: ReactNode
}) {
  if (nodes.length === 0)
    return <Text c="dimmed">The orchestrator has no SFU nodes.</Text>

  return (
    <Checkbox.Group description="Voice sessions of the coordinator run on these nodes. Sessions on a removed or disabled node move to another one."
      value={value} onChange={onChange} error={error}>
      <Stack gap="xs" mt="xs">
        {nodes.map(node => (
          <Checkbox key={node.id} value={String(node.id)}
            label={
              <Group gap="xs">
                <span>{node.name}</span>
                <Text span size="sm" c="dimmed">{node.host}:{node.port}</Text>
                <SfuNodeStatusBadge node={node} />
              </Group>
            } />
        ))}
      </Stack>
    </Checkbox.Group>
  )
}

function SaveStatus({ error, otherErrors, justSaved }: { error: Error | null, otherErrors: string[], justSaved: boolean }) {
  if (justSaved)
    return <Alert color="green">Saved and applied.</Alert>
  // A conflict is told by the notice of newer settings above
  if (!error || isConflict(error))
    return null

  // Field errors are shown next to their fields
  if (error instanceof ApiError && error.status === 400 && otherErrors.length === 0)
    return <Alert color="red">Some values are not valid; see the fields above.</Alert>

  return (
    <Alert color="red" title="Could not save">
      {otherErrors.length > 0 ? otherErrors.map(message => <div key={message}>{message}</div>) : error.message}
    </Alert>
  )
}

/** Hands validation errors of the server to the fields; the ones no field took are shown together. */
class FieldErrors {
  private readonly errors: Map<string, string[]>

  constructor(errors: Record<string, string[]>) {
    // The server names fields as in C# ("Heartbeat.TimeoutSeconds"); compare without case
    this.errors = new Map(Object.entries(errors).map(([key, messages]) => [key.toLowerCase(), messages]))
  }

  take(field: string): string | undefined {
    const key = field.toLowerCase()
    const messages = this.errors.get(key)
    this.errors.delete(key)
    return messages?.join(' ')
  }

  rest(): string[] {
    return [...this.errors.values()].flat()
  }
}
