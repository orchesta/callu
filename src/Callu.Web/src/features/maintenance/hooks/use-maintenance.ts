import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiQueryOptions, useApiMutation } from "@/shared/api";
import { maintenanceApi } from "../api/maintenance.api";
import type { MaintenanceWindowDto, CreateMaintenanceWindowRequest } from "../types/maintenance.types";

export const MW_KEY = ["maintenance-windows"] as const;
export const MW_ACTIVE_KEY = ["maintenance-windows", "active"] as const;

export const maintenanceQueries = {
    all: () => apiQueryOptions<MaintenanceWindowDto[]>(MW_KEY, () => maintenanceApi.getAll()),
    active: () =>
        apiQueryOptions<MaintenanceWindowDto[]>(MW_ACTIVE_KEY, () => maintenanceApi.getActive(), {
            refetchInterval: 60_000,
        }),
};

export function useMaintenanceWindows() {
  return useQuery(maintenanceQueries.all());
}

export function useCreateMaintenanceWindow() {
  const qc = useQueryClient();
  return useApiMutation<MaintenanceWindowDto, CreateMaintenanceWindowRequest>(
    (data) => maintenanceApi.create(data),
    {
      successMessage: "Maintenance window created",
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: MW_KEY });
        qc.invalidateQueries({ queryKey: MW_ACTIVE_KEY });
      },
    },
  );
}

export function useCancelMaintenanceWindow() {
  const qc = useQueryClient();
  return useApiMutation<void, string>(
    (id) => maintenanceApi.cancel(id),
    {
      successMessage: "Maintenance window cancelled",
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: MW_KEY });
        qc.invalidateQueries({ queryKey: MW_ACTIVE_KEY });
      },
    },
  );
}

export function useDeleteMaintenanceWindow() {
  const qc = useQueryClient();
  return useApiMutation<void, string>(
    (id) => maintenanceApi.delete(id),
    {
      successMessage: "Maintenance window deleted",
      onSuccess: () => qc.invalidateQueries({ queryKey: MW_KEY }),
    },
  );
}
