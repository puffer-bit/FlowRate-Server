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
    <MantineProvider defaultColorScheme="dark" theme={{
      // Primary and neutral shades match the client's standard theme.
      primaryColor: 'flowrate', primaryShade: { light: 6, dark: 6 }, defaultRadius: 'md',
      colors: {
        flowrate: ['#f0efff', '#e8e8ff', '#d0ccff', '#a39dff', '#8b7fff', '#7d75ff', '#6c63ff', '#5a52e0', '#4a43c0', '#3e3e6b'],
        dark: ['#e7e7e7', '#cbcbcb', '#aaa', '#727272', '#656565', '#3d3d3d', '#2f2f2f', '#232323', '#1e1e1e', '#161616'],
      },
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
