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
  /**
   * Explicit "apply to every service" toggle. Backend validator rejects requests
   * where this is false AND affectedServiceIds is empty (no more silent global
   * suppressions).
   */
  appliesToAllServices: boolean;
  mode: string;
}
