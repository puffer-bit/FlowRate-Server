/** "3d 04:05:06" style duration since an ISO timestamp. */
export function formatSince(isoUtc: string, now: number): string {
  const totalSeconds = Math.max(0, Math.floor((now - Date.parse(isoUtc)) / 1000))
  const days = Math.floor(totalSeconds / 86_400)
  const hours = Math.floor(totalSeconds / 3600) % 24
  const minutes = Math.floor(totalSeconds / 60) % 60
  const seconds = totalSeconds % 60
  const clock = [hours, minutes, seconds].map(part => String(part).padStart(2, '0')).join(':')
  return days > 0 ? `${days}d ${clock}` : clock
}

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'medium' })

export const formatDateTime = (isoUtc: string) => dateTime.format(new Date(isoUtc))
