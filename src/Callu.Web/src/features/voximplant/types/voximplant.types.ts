/**
 * Voximplant Management Types — mirrors backend DTOs from
 * Callu.Shared.Models.Communication
 */

/** Mirrors BE VoxAccountInfoDto */
export interface VoxAccountInfo {
    accountId: number;
    accountName: string;
    accountEmail: string;
    balance: number;
    currency: string;
    active: boolean;
}

export interface ProvisioningResource {
    name: string;
    type: 'application' | 'scenario' | 'rule' | 'user';
    exists: boolean;
    resourceId?: number;
}

export interface ProvisioningStatus {
    isProvisioned: boolean;
    accountInfo: VoxAccountInfo | null;
    resources: ProvisioningResource[];
    providerUserCount: number;
    calluUserCount: number;
    usersInSync: boolean;
    issues: string[];
}

/** Mirrors BE VoxApplicationDto */
export interface VoxApplicationDto {
    applicationId: number;
    applicationName: string;
    modified?: string;
    secureRecordStorage: boolean;
}

/** Mirrors BE VoxScenarioDto */
export interface VoxScenarioDto {
    scenarioId: number;
    scenarioName: string;
    modified?: string;
    scenarioScript?: string;
}

/** Mirrors BE VoxRuleDto */
export interface VoxRuleDto {
    ruleId: number;
    ruleName: string;
    rulePattern: string;
    rulePatternExclude?: string;
    videoConference: boolean;
    modified?: string;
    scenarios: VoxScenarioDto[];
}

/** Mirrors BE VoxUserDto */
export interface VoxUserDto {
    userId: number;
    userName: string;
    displayName: string;
    active: boolean;
    customData?: string;
}

/** Mirrors BE CreateVoxApplicationRequest */
export interface CreateVoxApplicationRequest {
    applicationName: string;
}

/** Mirrors BE CreateVoxScenarioRequest */
export interface CreateVoxScenarioRequest {
    applicationId?: number;
    name: string;
    script: string;
    rewrite: boolean;
    ruleId?: number;
}

/** Mirrors BE CreateVoxRuleRequest */
export interface CreateVoxRuleRequest {
    applicationId: number;
    name: string;
    pattern: string;
    scenarioIds: number[];
    videoConference: boolean;
}

/** Mirrors BE CreateVoxUserRequest */
export interface CreateVoxUserRequest {
    applicationId: number;
    userName: string;
    password: string;
    displayName: string;
}

/** Mirrors BE ProvisioningResult (from VoximplantProviderLifecycle) */
export interface VoxProvisionResult {
    success: boolean;
    error?: string;
    createdResources: string[];
    existingResources: string[];
}

/** Mirrors BE SyncResult (from VoximplantProviderLifecycle) */
export interface VoxUserSyncResult {
    success: boolean;
    usersCreated: number;
    usersDeleted: number;
    usersUnchanged: number;
    errors: string[];
}

