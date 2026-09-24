/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    rolldownOptions: {
      output: {
        codeSplitting: {
          // Both libraries are large, change rarely, and had been folded into app chunks past the
          // 500 kB warning: KaTeX (math in chat replies) was over half of ChatPage, and CI's build
          // merged three.module into SceneBackground. As their own chunks they load in parallel and
          // stay cached across deploys that only change app code. `maxSize` lets three's two build
          // files (core + module, ~360 kB each) stay separate rather than forming one 730 kB chunk.
          groups: [
            { name: 'katex', test: /node_modules[\\/]katex[\\/]/ },
            { name: 'three', test: /node_modules[\\/]three[\\/]/, maxSize: 450 * 1024 },
          ],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/setupTests.ts'],
    server: {
      deps: {
        // @react-three/* ship no `exports` map, so Node would load their CommonJS builds, which
        // `require('three')` — a second copy of three alongside the app's ESM one ("Multiple
        // instances of Three.js being imported"). Inlining them resolves their ESM builds instead.
        inline: [/@react-three[\\/]/],
      },
    },
  },
})
