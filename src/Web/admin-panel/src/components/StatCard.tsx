import { Paper, Text } from '@mantine/core'
import type { ReactNode } from 'react'

export function StatCard({ label, value }: { label: string, value: ReactNode }) {
  return (
    <Paper withBorder p="md" radius="md">
      <Text size="sm" c="dimmed">{label}</Text>
      <Text fz={28} fw={700} style={{ fontVariantNumeric: 'tabular-nums' }}>{value}</Text>
    </Paper>
  )
}
