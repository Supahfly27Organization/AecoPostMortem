import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Vitest reads this file instead of vite.config.ts when both exist, so the react plugin is
// repeated here rather than shared — the alternative (mergeConfig against vite.config.ts) pulls
// vite.config.ts's own config resolution into every test run for one plugin entry.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/vitest-setup.ts'],
    css: true,
  },
})
