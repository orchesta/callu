export interface EscalationPolicyDto {
    id: string;
    name: string;
    description?: string;
    teamId?: string;
    teamName?: string;
    isActive: boolean;
    stepCount: number;
    createdAt: string;
    steps?: EscalationStepDto[];
}

export interface EscalationPolicyDetailDto extends EscalationPolicyDto {
    steps: EscalationStepDto[];
}

export interface EscalationStepDto {
    id: string;
    escalationPolicyId: string;
    level: number;
    title: string;
    description?: string;
    delayMinutes: number;
    scheduleId?: string;
    scheduleName?: string;
    teamId?: string;
    teamName?: string;
    notifyAllTeamMembers?: boolean;
    /** When true and the step targets a schedule, both primary and secondary on-call are paged. */
    notifyBothOnCall?: boolean;
    notifyUserIds: string[];
    notifyUserNames: string[];
}

export interface CreateEscalationRequest {
    name: string;
    description?: string;
    teamId?: string;
    isActive?: boolean;
}

export interface UpdateEscalationRequest {
    name?: string;
    description?: string;
    teamId?: string;
    isActive?: boolean;
}

export interface AddStepRequest {
    level?: number;
    title: string;
    description?: string;
    delayMinutes: number;
    scheduleId?: string | null;
    teamId?: string | null;
    notifyAllTeamMembers?: boolean;
    notifyBothOnCall?: boolean;
    notifyUserIds?: string[];
}

export interface UpdateStepRequest {
    title?: string;
    description?: string;
    delayMinutes?: number;
    scheduleId?: string | null;
    teamId?: string | null;
    notifyAllTeamMembers?: boolean | null;
    notifyBothOnCall?: boolean | null;
    notifyUserIds?: string[];
}
