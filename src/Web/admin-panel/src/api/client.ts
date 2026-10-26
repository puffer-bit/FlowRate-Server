import type { components } from './schema'

type Schemas = components['schemas']

export type PanelOverview = Schemas['PanelOverviewDto']
export type CoordinatorSummary = Schemas['CoordinatorSummaryDto']
export type CoordinatorDetails = Schemas['CoordinatorDetailsDto']
export type HybridSession = Schemas['HybridSessionDto']
export type Peer = Schemas['PeerDto']
export type ConnectedUser = Schemas['ConnectedUserDto']
export type CoordinatorState = Schemas['CoordinatorState']
export type CoordinatorSettings = Schemas['CoordinatorSettingsDto']
export type UpdateCoordinatorSettings = Schemas['UpdateCoordinatorSettingsDto']
export type RateLimitSettings = Schemas['RateLimitSettingsDto']
export type SfuNode = Schemas['SfuNodeDto']
export type SfuNodeStatus = Schemas['SfuNodeStatusDto']
export type SfuNodeConnectionState = Schemas['SfuNodeConnectionState']
export type Role = Schemas['RoleDto']
export type RoleChange = Schemas['RoleChangeDto']
export type UpdateRole = Schemas['UpdateRoleDto']
export type Permission = Schemas['Permission']
export type CoordinatorUser = Schemas['CoordinatorUserDto']
export type UserSecurity = Schemas['UserSecurityDto']
export type Passkey = Schemas['PasskeyDto']
export type TwoFactorOffer = Schemas['TwoFactorOfferDto']

export class ApiError extends Error {
  readonly status: number
  /** Validation errors of a 400 answer, by field ("Heartbeat.TimeoutSeconds"; "" for the whole request). */
  readonly errors: Record<string, string[]>

  constructor(status: number, message: string, errors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.errors = errors
  }
}

/** The REST API of the panel; the server keeps the paths in PanelRoutes. */
const apiBase = '/api/panel'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}) },
  })
  if (!response.ok)
    throw await toApiError(response)
  if (response.status === 204)
    return undefined as T
  return (await response.json()) as T
}

async function toApiError(response: Response): Promise<ApiError> {
  const fallback = `${response.status} ${response.statusText}`
  try {
    // ASP.NET Core answers with problem details
    const problem = (await response.json()) as { title?: string, errors?: Record<string, string[]> }
    return new ApiError(response.status, problem.title ?? fallback, problem.errors ?? {})
  } catch {
    return new ApiError(response.status, fallback)
  }
}

/** The server does not know what was asked for: a wrong link, or a coordinator that is not running. */
export const isNotFound = (error: unknown) => error instanceof ApiError && error.status === 404

/** Someone else saved first; nothing was saved. */
export const isConflict = (error: unknown) => error instanceof ApiError && error.status === 409

const get = <T>(path: string) => request<T>(path)
const send = <T>(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown) =>
  request<T>(path, { method, body: body === undefined ? undefined : JSON.stringify(body) })

const coordinatorPath = (id: string) => `/coordinators/${encodeURIComponent(id)}`

export const getOverview = () => get<PanelOverview>('/overview')

export const getSfuNodes = () => get<SfuNode[]>('/sfu-nodes')

export const getCoordinator = (id: string) => get<CoordinatorDetails>(coordinatorPath(id))

export const getCoordinatorSettings = (id: string) => get<CoordinatorSettings>(`${coordinatorPath(id)}/settings`)

export const updateCoordinatorSettings = (id: string, settings: UpdateCoordinatorSettings) =>
  send<CoordinatorSettings>('PUT', `${coordinatorPath(id)}/settings`, settings)

/** All roles, lowest first. */
export const getRoles = (id: string) => get<Role[]>(`${coordinatorPath(id)}/roles`)

export const createRole = (id: string, role: RoleChange) => send<Role>('POST', `${coordinatorPath(id)}/roles`, role)

export const updateRole = (id: string, roleId: number, role: UpdateRole) =>
  send<Role>('PUT', `${coordinatorPath(id)}/roles/${roleId}`, role)

export const deleteRole = (id: string, roleId: number) => send<void>('DELETE', `${coordinatorPath(id)}/roles/${roleId}`)

/** Every role but the everyone role, lowest first. */
export const reorderRoles = (id: string, roleIds: number[]) =>
  send<Role[]>('PUT', `${coordinatorPath(id)}/roles/order`, { roleIds })

export const getCoordinatorUsers = (id: string) => get<CoordinatorUser[]>(`${coordinatorPath(id)}/users`)

const userRolePath = (id: string, userId: number, roleId: number) =>
  `${coordinatorPath(id)}/users/${userId}/roles/${roleId}`

/** Answers with the roles the user has now. */
export const assignRole = (id: string, userId: number, roleId: number) =>
  send<number[]>('PUT', userRolePath(id, userId, roleId))

/** Answers with the roles the user has now. */
export const unassignRole = (id: string, userId: number, roleId: number) =>
  send<number[]>('DELETE', userRolePath(id, userId, roleId))

const securityPath = (id: string, userId: number) => `${coordinatorPath(id)}/users/${userId}/security`

/** What the user logs in with: password, one-time codes, passkeys. */
export const getUserSecurity = (id: string, userId: number) => get<UserSecurity>(securityPath(id, userId))

/** Null removes the password. */
export const setUserPassword = (id: string, userId: number, password: string | null) =>
  send<UserSecurity>('PUT', `${securityPath(id, userId)}/password`, { password })

/** A new secret; codes are on once one made from it is confirmed. */
export const offerTwoFactor = (id: string, userId: number) =>
  send<TwoFactorOffer>('POST', `${securityPath(id, userId)}/two-factor`)

export const confirmTwoFactor = (id: string, userId: number, code: string) =>
  send<UserSecurity>('POST', `${securityPath(id, userId)}/two-factor/confirm`, { code })

export const disableTwoFactor = (id: string, userId: number) =>
  send<UserSecurity>('DELETE', `${securityPath(id, userId)}/two-factor`)

/** Options for navigator.credentials.create, binary values in base64url. */
export const getPasskeyOptions = (id: string, userId: number) =>
  send<Record<string, unknown>>('POST', `${securityPath(id, userId)}/passkeys/options`)

export const registerPasskey = (id: string, userId: number, label: string | null, credential: unknown) =>
  send<UserSecurity>('POST', `${securityPath(id, userId)}/passkeys`, { label, credential })

export const deletePasskey = (id: string, userId: number, passkeyId: number) =>
  send<UserSecurity>('DELETE', `${securityPath(id, userId)}/passkeys/${passkeyId}`)

// Everything about one coordinator starts with ['coordinator', id]
export const queryKeys = {
  overview: ['overview'] as const,
  coordinator: (id: string) => ['coordinator', id] as const,
  coordinatorSettings: (id: string) => ['coordinator', id, 'settings'] as const,
  sfuNodes: ['sfuNodes'] as const,
  roles: (id: string) => ['coordinator', id, 'roles'] as const,
  coordinatorUsers: (id: string) => ['coordinator', id, 'users'] as const,
  userSecurity: (id: string, userId: number) => ['coordinator', id, 'users', userId, 'security'] as const,
}
