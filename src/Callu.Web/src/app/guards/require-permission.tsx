import { Navigate } from "react-router";
import { useAuth } from "@/shared/auth/auth.context";
import { hasPermission, type Permission } from "@/shared/auth/roles";

interface RequirePermissionProps {
  permission: Permission;
  children: React.ReactNode;
}

/** Route guard mirroring the claim the API requires, so the UI does not open a page that only 403s. */
export function RequirePermission({ permission, children }: RequirePermissionProps) {
  const { user } = useAuth();

  if (!hasPermission(user?.role, permission)) {
    return <Navigate to="/auth/access-denied" replace />;
  }

  return <>{children}</>;
}
