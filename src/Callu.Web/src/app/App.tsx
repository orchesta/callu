import { RouterProvider } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { useEffect } from 'react';
import { router } from './routes';
import { ErrorBoundary } from '@/shared/components/error-boundary';
import { AuthProvider } from '@/shared/auth/auth.context';
import { Toaster } from '@/shared/components/ui/sonner';
import { initBundleMonitoring } from '@/shared/utils/bundle-analyzer';
import { queryClient } from '@/shared/api/query-client';
import '../styles/index.css';

export default function App() {
  useEffect(() => {
    if (import.meta.env.DEV) {
      initBundleMonitoring();
    }
  }, []);

  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <RouterProvider router={router} />
          <Toaster
            position="bottom-right"
            expand={false}
            richColors
            closeButton
            duration={4000}
          />
        </AuthProvider>
      </QueryClientProvider>
    </ErrorBoundary>
  );
}