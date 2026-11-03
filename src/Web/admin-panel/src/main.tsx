import '@mantine/core/styles.css'
import './panel.css'
import { MantineProvider } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { App } from './App'

const queryClient = new QueryClient({
  defaultOptions: {
    // Live updates refetch on change, so cached data stays fresh until then
    queries: { staleTime: Infinity, retry: 1 },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MantineProvider defaultColorScheme="auto" theme={{
      primaryColor: 'teal', defaultRadius: 'md',
      fontFamily: "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
      spacing: { xs: '0.5rem', sm: '0.625rem', md: '0.75rem', lg: '1rem', xl: '1.25rem' },
      headings: { fontFamily: "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", fontWeight: '650' },
      components: {
        Button: { defaultProps: { radius: 'md', size: 'xs' } },
        Table: { defaultProps: { striped: false, highlightOnHover: true, verticalSpacing: 'xs', horizontalSpacing: 'sm' } },
      },
    }}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter basename="/panel">
          <App />
        </BrowserRouter>
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
)
