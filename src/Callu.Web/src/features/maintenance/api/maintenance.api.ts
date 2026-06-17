import { apiClient } from "@/shared/api/client";
import type { MaintenanceWindowDto, CreateMaintenanceWindowRequest } from "../types/maintenance.types";

const BASE = "/api/v1/maintenance-windows";

export const maintenanceApi = {
  /** List all maintenance windows (ordered by StartsAt desc) */
  getAll: () => apiClient.get<MaintenanceWindowDto[]>(BASE),

  /** List currently active windows */
  getActive: () => apiClient.get<MaintenanceWindowDto[]>(`${BASE}/active`),

  /** Create a new maintenance window */
  create: (data: CreateMaintenanceWindowRequest) =>
    apiClient.post<MaintenanceWindowDto>(BASE, data),

  /** Cancel a window (sets IsCancelled = true) */
  cancel: (id: string) => apiClient.post<void>(`${BASE}/${id}/cancel`, {}),

  /** Permanently delete a window */
  delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
};
