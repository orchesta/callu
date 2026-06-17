import { Outlet, Navigate } from "react-router";
import { AppShell } from "@/shared/components/layout/app-shell";
import { useAuth } from "@/shared/auth/auth.context";
import { LoadingState } from "@/shared/components/loading-state";
import { t } from "@/shared/locales/i18n";

export function RootLayout() {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <LoadingState message={t("common.authenticating")} />;
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
