import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { serviceApi } from '../api/service.api';
import type {
    ServiceDto,
    ServiceListDto,
    ServiceDependencyDto,
    CreateServiceRequest,
    UpdateServiceRequest,
} from '../types/service.types';
import type { PagedResult } from '@/shared/types/common.types';

export const serviceKeys = {
    all: ['services'] as const,
    lists: () => [...serviceKeys.all, 'list'] as const,
    list: (filters: string) => [...serviceKeys.lists(), { filters }] as const,
    details: () => [...serviceKeys.all, 'detail'] as const,
    detail: (id: string) => [...serviceKeys.details(), id] as const,
    dependencies: (id: string) => [...serviceKeys.all, 'dependencies', id] as const,
};

export const serviceQueries = {
    page: (page: number, pageSize: number) =>
        apiQueryOptions<PagedResult<ServiceListDto>>(
            [...serviceKeys.lists(), page, pageSize],
            () => serviceApi.getAll(page, pageSize),
            { staleTime: 2 * 60_000 },
        ),
    detail: (id: string) =>
        apiQueryOptions<ServiceDto>(serviceKeys.detail(id), () => serviceApi.getById(id), { enabled: !!id }),
    dependencies: (serviceId: string) =>
        apiQueryOptions<ServiceDependencyDto[]>(
            serviceKeys.dependencies(serviceId),
            () => serviceApi.getDependencies(serviceId),
            { enabled: !!serviceId },
        ),
};

/**
 * Hook for fetching a single page of services with metadata
 */
export function useServicesPage(page = 1, pageSize = 20) {
    return useQuery(serviceQueries.page(page, pageSize));
}

/**
 * Hook for fetching all services (compact list)
 */
export function useServices() {
    const query = useServicesPage(1, 1000);

    return {
        ...query,
        data: query.data?.items ?? ([] as ServiceListDto[])
    };
}

/**
 * Hook for fetching a single service by ID
 */
export function useService(id: string) {
    return useQuery(serviceQueries.detail(id));
}

/**
 * Hook for fetching service dependencies
 */
export function useServiceDependencies(serviceId: string) {
    return useQuery(serviceQueries.dependencies(serviceId));
}

export function useCreateService() {
    const queryClient = useQueryClient();
    return useApiMutation<ServiceDto, CreateServiceRequest>(
        (data: CreateServiceRequest) => serviceApi.create(data),
        {
            successMessage: 'Service created successfully',
            onSuccess: () => {
                queryClient.invalidateQueries({ queryKey: serviceKeys.lists() });
            },
        }
    );
}

export function useUpdateService() {
    const queryClient = useQueryClient();
    return useApiMutation<ServiceDto, { id: string; data: UpdateServiceRequest }>(
        ({ id, data }) => serviceApi.update(id, data),
        {
            successMessage: 'Service updated successfully',
            onSuccess: (_: ServiceDto, { id }: { id: string }) => {
                queryClient.invalidateQueries({ queryKey: serviceKeys.all });
                queryClient.invalidateQueries({ queryKey: serviceKeys.detail(id) });
            },
        }
    );
}

export function useDeleteService() {
    const queryClient = useQueryClient();
    return useApiMutation<void, string>(
        (id: string) => serviceApi.delete(id),
        {
            successMessage: 'Service deleted successfully',
            onSuccess: () => {
                queryClient.invalidateQueries({ queryKey: serviceKeys.lists() });
            },
        }
    );
}

export function useAddDependency() {
    const queryClient = useQueryClient();
    return useApiMutation<ServiceDependencyDto, { serviceId: string; dependsOnServiceId: string; type: string; criticality: string; description?: string }>(
        ({ serviceId, ...data }) => serviceApi.addDependency(serviceId, data),
        {
            successMessage: 'Dependency added',
            onSuccess: () => {
                queryClient.invalidateQueries({ queryKey: serviceKeys.all });
            },
        }
    );
}

export function useRemoveDependency() {
    const queryClient = useQueryClient();
    return useApiMutation<void, string>(
        (dependencyId: string) => serviceApi.removeDependency(dependencyId),
        {
            successMessage: 'Dependency removed',
            onSuccess: () => {
                queryClient.invalidateQueries({ queryKey: serviceKeys.all });
            },
        }
    );
}
