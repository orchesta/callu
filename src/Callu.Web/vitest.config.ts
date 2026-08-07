import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'path';

export default defineConfig({
  plugins: [react()],
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: true,
    // jsdom setup alone runs into tens of seconds per file on a loaded machine, and userEvent
    // adds its own waits on top; the 5s default fails those runs for being slow, not wrong.
    testTimeout: 20_000,
    hookTimeout: 20_000,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        'node_modules/',
        'src/test/',
        '**/*.d.ts',
        '**/*.config.*',
        '**/mockData',
        '**/*.test.{ts,tsx}',
        // Type-only modules and barrels compile to no statements.
        'src/**/types/**',
        'src/**/*.types.ts',
        'src/**/index.ts',
        'src/main.tsx',
        'src/vite-env.d.ts',
      ],
      // A floor, not a score: measured against the whole `src/` tree, so it reads low.
      // Raise it as coverage grows — never lower it to make a run go green.
      thresholds: {
        statements: 26.9,
        branches: 28.5,
        functions: 17.1,
        lines: 27.5,
      },
    },
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
});
