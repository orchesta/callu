import { apiClient } from '@/shared/api';
import type {
    EscalationPolicyDto,
    EscalationPolicyDetailDto,
    EscalationStepDto,
    CreateEscalationRequest,
    UpdateEscalationRequest,
    AddStepRequest,
    UpdateStepRequest,
} from '../types/escalation.types';

const BASE = '/api/v1/escalations';

export const escalationApi = {
    getAll: () => apiClient.get<EscalationPolicyDto[]>(BASE),
    getById: (id: string) => apiClient.get<EscalationPolicyDetailDto>(`${BASE}/${id}`),
    create: (data: CreateEscalationRequest) => apiClient.post<EscalationPolicyDto>(BASE, data),
    update: (id: string, data: UpdateEscalationRequest) => apiClient.put<void>(`${BASE}/${id}`, data),
    delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),

    getSteps: (policyId: string) => apiClient.get<EscalationStepDto[]>(`${BASE}/${policyId}/steps`),
    addStep: (policyId: string, data: AddStepRequest) =>
        apiClient.post<EscalationStepDto>(`${BASE}/${policyId}/steps`, data),
    updateStep: (policyId: string, stepId: string, data: UpdateStepRequest) =>
        apiClient.put<void>(`${BASE}/${policyId}/steps/${stepId}`, data),
    removeStep: (policyId: string, stepId: string) =>
        apiClient.delete<void>(`${BASE}/${policyId}/steps/${stepId}`),
    reorderSteps: (policyId: string, stepIds: string[]) =>
        apiClient.put<void>(`${BASE}/${policyId}/steps/reorder`, { stepIds }),
};
