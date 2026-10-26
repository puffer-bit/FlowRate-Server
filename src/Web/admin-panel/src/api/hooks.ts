import { useEffect, useState } from 'react'
import { hashKey, useMutation, useQuery, useQueryClient, type QueryKey } from '@tanstack/react-query'
import {
  assignRole,
  confirmTwoFactor,
  createRole,
  deletePasskey,
  deleteRole,
  disableTwoFactor,
  getCoordinator,
  getCoordinatorUsers,
  getRoles,
  getCoordinatorSettings,
  getOverview,
  getSfuNodes,
  getPasskeyOptions,
  getUserSecurity,
  isConflict,
  offerTwoFactor,
  queryKeys,
  registerPasskey,
  reorderRoles,
  setUserPassword,
  unassignRole,
  updateCoordinatorSettings,
  updateRole,
  type RoleChange,
  type UpdateCoordinatorSettings,
  type UpdateRole,
  type UserSecurity,
} from './client'
import { createPasskey } from './webauthn'
import { onStatusChange, subscribe, type PanelEvent, type RealtimeStatus } from './realtime'

// The server coalesces signals too; this catches the ones that come right after each other
const refetchDebounceMs = 300

// Every change is signalled, see useLiveUpdates; only the SFU round trips are polled
export const useOverview = () => useQuery({ queryKey: queryKeys.overview, queryFn: getOverview })

export const useCoordinator = (id: string) =>
  useQuery({ queryKey: queryKeys.coordinator(id), queryFn: () => getCoordinator(id) })

export const useCoordinatorSettings = (id: string) =>
  useQuery({ queryKey: queryKeys.coordinatorSettings(id), queryFn: () => getCoordinatorSettings(id) })

// Connection changes are signalled; the round trip changes on every ping, so it is polled
const sfuNodesPollMs = 5_000

export const useSfuNodes = () =>
  useQuery({ queryKey: queryKeys.sfuNodes, queryFn: getSfuNodes, refetchInterval: sfuNodesPollMs })

export function useUpdateCoordinatorSettings(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (settings: UpdateCoordinatorSettings) => updateCoordinatorSettings(id, settings),
    onSuccess: saved => {
      queryClient.setQueryData(queryKeys.coordinatorSettings(id), saved)
      // The name, the endpoint or the state may have changed
      void queryClient.invalidateQueries({ queryKey: queryKeys.overview })
      void queryClient.invalidateQueries({ queryKey: queryKeys.coordinator(id), exact: true })
    },
    onError: error => {
      // Someone else saved first: fetch their settings, the form tells what happened
      if (isConflict(error))
        void queryClient.invalidateQueries({ queryKey: queryKeys.coordinatorSettings(id), exact: true })
    },
  })
}

export const useRoles = (id: string) =>
  useQuery({ queryKey: queryKeys.roles(id), queryFn: () => getRoles(id) })

export const useCoordinatorUsers = (id: string) =>
  useQuery({ queryKey: queryKeys.coordinatorUsers(id), queryFn: () => getCoordinatorUsers(id) })

/** Any role change: the roles and who has them are fetched again, whatever the answer. */
function useRoleMutation<T>(id: string, mutationFn: (variables: T) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roles(id), exact: true })
      void queryClient.invalidateQueries({ queryKey: queryKeys.coordinatorUsers(id), exact: true })
    },
  })
}

export const useCreateRole = (id: string) => useRoleMutation(id, (role: RoleChange) => createRole(id, role))

export const useUpdateRole = (id: string) =>
  useRoleMutation(id, ({ roleId, role }: { roleId: number, role: UpdateRole }) => updateRole(id, roleId, role))

export const useDeleteRole = (id: string) => useRoleMutation(id, (roleId: number) => deleteRole(id, roleId))

export const useReorderRoles = (id: string) => useRoleMutation(id, (roleIds: number[]) => reorderRoles(id, roleIds))

export const useSetUserRole = (id: string) =>
  useRoleMutation(id, ({ userId, roleId, has }: { userId: number, roleId: number, has: boolean }) =>
    has ? assignRole(id, userId, roleId) : unassignRole(id, userId, roleId))

export const useUserSecurity = (id: string, userId: number) =>
  useQuery({ queryKey: queryKeys.userSecurity(id, userId), queryFn: () => getUserSecurity(id, userId) })

/** A change of what a user logs in with; the answer is the new state, kept as it is. */
function useSecurityMutation<T>(id: string, userId: number, mutationFn: (variables: T) => Promise<UserSecurity>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSuccess: security => queryClient.setQueryData(queryKeys.userSecurity(id, userId), security),
  })
}

export const useSetUserPassword = (id: string, userId: number) =>
  useSecurityMutation(id, userId, (password: string | null) => setUserPassword(id, userId, password))

export const useOfferTwoFactor = (id: string, userId: number) =>
  useMutation({ mutationFn: () => offerTwoFactor(id, userId) })

export const useConfirmTwoFactor = (id: string, userId: number) =>
  useSecurityMutation(id, userId, (code: string) => confirmTwoFactor(id, userId, code))

export const useDisableTwoFactor = (id: string, userId: number) =>
  useSecurityMutation(id, userId, () => disableTwoFactor(id, userId))

/** Asks the server for options, lets the browser make the credential, and registers it. */
export const useAddPasskey = (id: string, userId: number) =>
  useSecurityMutation(id, userId, async (label: string | null) => {
    const credential = await createPasskey(await getPasskeyOptions(id, userId))
    return registerPasskey(id, userId, label, credential)
  })

export const useDeletePasskey = (id: string, userId: number) =>
  useSecurityMutation(id, userId, (passkeyId: number) => deletePasskey(id, userId, passkeyId))

/** What a change signal makes stale; 'all' when the panel may have missed signals. */
function staleQueries(event: PanelEvent): QueryKey[] | 'all' {
  const id = event.coordinatorId
  switch (event.type) {
    case 'connected':
      return 'all'
    case 'coordinatorChanged':
      // The totals and the row of the coordinator on the overview change with it
      return id ? [queryKeys.overview, queryKeys.coordinator(id)] : [queryKeys.overview]
    case 'coordinatorSettingsChanged':
      // A form being edited keeps its values and tells that the settings changed elsewhere
      return id ? [queryKeys.coordinatorSettings(id)] : []
    case 'coordinatorRolesChanged':
      return id ? [queryKeys.roles(id), queryKeys.coordinatorUsers(id)] : []
    case 'sfuNodesChanged':
      return [queryKeys.sfuNodes]
  }
}

/** Refetches what the server says changed, a short burst of signals at once. Mount once, near the root. */
export function useLiveUpdates() {
  const queryClient = useQueryClient()

  useEffect(() => {
    // By the hash of the key, so the same query is refetched once per burst
    const pending = new Map<string, QueryKey>()
    let refetchAll = false
    let timer: ReturnType<typeof setTimeout> | undefined

    const flush = () => {
      timer = undefined
      if (refetchAll)
        void queryClient.invalidateQueries()
      else
        pending.forEach(queryKey => void queryClient.invalidateQueries({ queryKey, exact: true }))
      pending.clear()
      refetchAll = false
    }

    const unsubscribe = subscribe(event => {
      const stale = staleQueries(event)
      if (stale === 'all')
        refetchAll = true
      else if (stale.length > 0)
        stale.forEach(queryKey => pending.set(hashKey(queryKey), queryKey))
      else
        return
      timer ??= setTimeout(flush, refetchDebounceMs)
    })

    return () => {
      unsubscribe()
      clearTimeout(timer)
    }
  }, [queryClient])
}

export function useRealtimeStatus(): RealtimeStatus {
  const [status, setStatus] = useState<RealtimeStatus>('connecting')
  useEffect(() => onStatusChange(setStatus), [])
  return status
}

/** The current time, updated every second, for live durations. */
export function useNow(): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])
  return now
}
