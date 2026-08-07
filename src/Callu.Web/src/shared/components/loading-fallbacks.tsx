/**
 * Loading Fallback Components
 * Used by React Suspense for lazy-loaded routes
 */

import { t } from '@/shared/locales/i18n';

/**
 * Full page loading spinner for route transitions
 */
export function RouteLoadingFallback() {
  return (
    <div className="flex items-center justify-center min-h-[calc(100vh-4rem)] bg-background">
      <div className="flex flex-col items-center gap-4">
        <div className="relative">
          <div className="w-12 h-12 rounded-full border-2 border-brand-500/20" />
          <div className="absolute inset-0 w-12 h-12 rounded-full border-2 border-brand-500 border-t-transparent animate-spin" />
        </div>
        <p className="text-sm text-muted-foreground">{t("shared.loading.routeFallback")}</p>
      </div>
    </div>
  );
}