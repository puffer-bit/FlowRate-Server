// The only module that knows how change signals reach the browser (server-sent events now).
// Everything else subscribes here, so the transport can be swapped without touching the UI.
import type { components } from './schema'

// Names the server sends, see PanelEventTypes in Server.Asp; "ping" only keeps the stream open
const eventTypes = [
  'connected', 'coordinatorChanged', 'coordinatorSettingsChanged', 'coordinatorRolesChanged', 'sfuNodesChanged',
] as const

export type PanelEventType = (typeof eventTypes)[number]
export type PanelEvent = Omit<components['schemas']['PanelEvent'], 'type'> & { type: PanelEventType }
export type RealtimeStatus = 'connecting' | 'open' | 'reconnecting'

// PanelRoutes.Events on the server
const eventsUrl = '/api/panel/events'

type EventListener = (event: PanelEvent) => void
type StatusListener = (status: RealtimeStatus) => void

const eventListeners = new Set<EventListener>()
const statusListeners = new Set<StatusListener>()
let source: EventSource | null = null
let status: RealtimeStatus = 'connecting'

function setStatus(next: RealtimeStatus) {
  status = next
  statusListeners.forEach(listener => listener(next))
}

function open() {
  source = new EventSource(eventsUrl)
  source.onopen = () => setStatus('open')
  // EventSource reconnects by itself; the server sends "connected" again once it is back
  source.onerror = () => setStatus('reconnecting')
  for (const type of eventTypes) {
    source.addEventListener(type, message => {
      const event = JSON.parse((message as MessageEvent<string>).data) as PanelEvent
      eventListeners.forEach(listener => listener(event))
    })
  }
}

function closeIfUnused() {
  if (eventListeners.size > 0 || statusListeners.size > 0 || !source)
    return
  source.close()
  source = null
  status = 'connecting'
}

export function subscribe(listener: EventListener): () => void {
  eventListeners.add(listener)
  if (!source)
    open()
  return () => {
    eventListeners.delete(listener)
    closeIfUnused()
  }
}

export function onStatusChange(listener: StatusListener): () => void {
  statusListeners.add(listener)
  listener(status)
  return () => {
    statusListeners.delete(listener)
    closeIfUnused()
  }
}
