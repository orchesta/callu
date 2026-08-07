export interface MaintenanceWindowDto {
  id: string;
  title: string;
  description?: string;
  startsAt: string;
  endsAt: string;
  affectedServiceIds: string[];
  /** When true, the window covers every service; affectedServiceIds is ignored. */
  appliesToAllServices: boolean;
  mode: "SuppressAlerts" | "AutoAcknowledge" | string;
  createdById: string;
  isCancelled: boolean;
  isActive: boolean;
  createdAt: string;
}

export interface CreateMaintenanceWindowRequest {
  title: string;
  description?: string;
  startsAt: string;
  endsAt: string;
  affectedServiceIds: string[];
  /** Explicit "apply to every service" toggle; the backend rejects a window that is neither global
   * nor scoped to at least one service, so suppression is never silently unbounded. */
  appliesToAllServices: boolean;
  mode: string;
}

export type UpdateMaintenanceWindowRequest = CreateMaintenanceWindowRequest;
