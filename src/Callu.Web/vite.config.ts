import { defineConfig } from 'vite'
import path from 'path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [
    // The React and Tailwind plugins are both required for Make, even if
    // Tailwind is not being actively used – do not remove them
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      // Alias @ to the src directory
      '@': path.resolve(__dirname, './src'),
    },
    // Ensure singleton instances of these packages to prevent duplicate React contexts
    dedupe: [
      'react',
      'react-dom',
      'react-router',
    ],
  },

  // File types to support raw imports. Never add .css, .tsx, or .ts files to this.
  assetsInclude: ['**/*.svg', '**/*.csv'],

  // Build optimization
  build: {
    // Target modern browsers
    target: 'esnext',
    
    // Enable minification
    minify: 'esbuild',
    
    // Source maps for production debugging
    sourcemap: false,
    
    // Chunk size warnings
    chunkSizeWarningLimit: 1000,
    
    // Rollup options for code splitting
    rollupOptions: {
      output: {
        // Manual chunk splitting for better caching
        manualChunks: {
          // React ecosystem
          'react-vendor': ['react', 'react-dom', 'react-router'],
          
          // UI libraries
          'ui-vendor': [
            '@radix-ui/react-dialog',
            '@radix-ui/react-dropdown-menu',
            '@radix-ui/react-select',
            '@radix-ui/react-tabs',
            '@radix-ui/react-tooltip',
          ],
          
          // Form & validation
          'form-vendor': ['react-hook-form', '@hookform/resolvers', 'zod'],
          
          // Data fetching
          'query-vendor': ['@tanstack/react-query'],
          
          // Icons & animations
          'assets-vendor': ['lucide-react', 'motion'],
          
          // Charts
          'charts-vendor': ['recharts'],
        },
        
        // File naming
        chunkFileNames: 'assets/[name]-[hash].js',
        entryFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]',
      },
    },
    
    // Asset inlining threshold (10kb)
    assetsInlineLimit: 10240,
  },

  // Development server
  server: {
    port: 3000,
    strictPort: false,
    host: true,
    open: true,
  },

  // Preview server (for production builds)
  preview: {
    port: 3000,
    strictPort: false,
    host: true,
  },

  // Dependency optimization
  optimizeDeps: {
    include: [
      'react',
      'react-dom',
      'react-router',
      '@tanstack/react-query',
      'lucide-react',
    ],
  },
})