import { apiClient } from "@/shared/api/client";
import type {
  MaintenanceWindowDto,
  CreateMaintenanceWindowRequest,
  UpdateMaintenanceWindowRequest,
} from "../types/maintenance.types";

const BASE = "/api/v1/maintenance-windows";

export const maintenanceApi = {
  getAll: () => apiClient.get<MaintenanceWindowDto[]>(BASE),

  getActive: () => apiClient.get<MaintenanceWindowDto[]>(`${BASE}/active`),

  create: (data: CreateMaintenanceWindowRequest) =>
    apiClient.post<MaintenanceWindowDto>(BASE, data),

  update: (id: string, data: UpdateMaintenanceWindowRequest) =>
    apiClient.put<MaintenanceWindowDto>(`${BASE}/${id}`, data),

  cancel: (id: string) => apiClient.post<void>(`${BASE}/${id}/cancel`, {}),

  delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
};
