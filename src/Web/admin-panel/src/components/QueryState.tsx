import { Alert, Center, Loader } from '@mantine/core'
import type { UseQueryResult } from '@tanstack/react-query'
import type { ReactNode } from 'react'

/** Shows a loader or an error until the query has data, then renders the children with it. */
export function QueryState<T>({ query, children }: { query: UseQueryResult<T>, children: (data: T) => ReactNode }) {
  if (query.data !== undefined)
    return children(query.data)
  if (query.isError)
    return <Alert color="red" title="Could not load data">{query.error.message}</Alert>
  return <Center py="xl"><Loader /></Center>
}
