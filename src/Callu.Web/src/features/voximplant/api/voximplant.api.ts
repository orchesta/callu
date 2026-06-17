/**
 * Voximplant Management API — connects to VoximplantController.
 *
 * Base: /api/v1/voximplant/management
 *
 * Lifecycle Endpoints (via VoximplantProviderLifecycle):
 *   GET    /{providerId}/status       → ProvisioningStatus
 *   POST   /{providerId}/provision    → ProvisioningResult
 *   POST   /{providerId}/sync-users   → SyncResult
 *
 * Management Endpoints (via VoximplantManagementService):
 *   GET    /{providerId}/account      → VoxAccountInfo
 *   GET/POST/DELETE applications, scenarios, rules, users
 */

import { apiClient } from '@/shared/api';
import type {
    VoxAccountInfo,
    VoxApplicationDto,
    VoxScenarioDto,
    VoxRuleDto,
    VoxUserDto,
    CreateVoxApplicationRequest,
    CreateVoxScenarioRequest,
    CreateVoxRuleRequest,
    CreateVoxUserRequest,
    VoxProvisionResult,
    VoxUserSyncResult,
    ProvisioningStatus,
} from '../types/voximplant.types';

const BASE = '/api/v1/voximplant/management';

export const voximplantApi = {
    /** GET /{providerId}/account */
    getAccountInfo: (providerId: string) =>
        apiClient.get<VoxAccountInfo>(`${BASE}/${providerId}/account`),

    /** GET /{providerId}/applications */
    getApplications: (providerId: string) =>
        apiClient.get<VoxApplicationDto[]>(`${BASE}/${providerId}/applications`),

    /** POST /{providerId}/applications */
    createApplication: (providerId: string, data: CreateVoxApplicationRequest) =>
        apiClient.post<VoxApplicationDto>(`${BASE}/${providerId}/applications`, data),

    /** DELETE /{providerId}/applications/{applicationId} */
    deleteApplication: (providerId: string, applicationId: number) =>
        apiClient.delete<void>(`${BASE}/${providerId}/applications/${applicationId}`),

    /** GET /{providerId}/scenarios?applicationId= */
    getScenarios: (providerId: string, applicationId?: number) =>
        apiClient.get<VoxScenarioDto[]>(
            `${BASE}/${providerId}/scenarios${applicationId ? `?applicationId=${applicationId}` : ''}`,
        ),

    /** POST /{providerId}/scenarios */
    createScenario: (providerId: string, data: CreateVoxScenarioRequest) =>
        apiClient.post<VoxScenarioDto>(`${BASE}/${providerId}/scenarios`, data),

    /** GET /{providerId}/applications/{applicationId}/rules */
    getRules: (providerId: string, applicationId: number) =>
        apiClient.get<VoxRuleDto[]>(`${BASE}/${providerId}/applications/${applicationId}/rules`),

    /** POST /{providerId}/rules */
    createRule: (providerId: string, data: CreateVoxRuleRequest) =>
        apiClient.post<VoxRuleDto>(`${BASE}/${providerId}/rules`, data),

    /** GET /{providerId}/applications/{applicationId}/users */
    getUsers: (providerId: string, applicationId: number) =>
        apiClient.get<VoxUserDto[]>(`${BASE}/${providerId}/applications/${applicationId}/users`),

    /** POST /{providerId}/users */
    createUser: (providerId: string, data: CreateVoxUserRequest) =>
        apiClient.post<VoxUserDto>(`${BASE}/${providerId}/users`, data),

    /** GET /{providerId}/status — full provisioning status */
    getStatus: (providerId: string) =>
        apiClient.get<ProvisioningStatus>(`${BASE}/${providerId}/status`),

    /** POST /{providerId}/provision */
    provision: (providerId: string) =>
        apiClient.post<VoxProvisionResult>(`${BASE}/${providerId}/provision`),

    /** POST /{providerId}/sync-users */
    syncUsers: (providerId: string) =>
        apiClient.post<VoxUserSyncResult>(
            `${BASE}/${providerId}/sync-users`,
        ),
};
