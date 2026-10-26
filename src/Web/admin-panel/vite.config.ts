import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The server the dev proxy forwards /api to; override with PANEL_API=https://host:port
const api = process.env.PANEL_API ?? 'https://localhost:7114'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Served by Server.Asp under /panel/
  base: '/panel/',
  build: {
    outDir: '../../Server/Server.Asp/Web/wwwroot/panel',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      // The development server uses a self-signed certificate
      '/api': { target: api, changeOrigin: true, secure: false },
    },
  },
})
