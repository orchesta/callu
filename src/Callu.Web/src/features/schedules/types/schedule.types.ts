export type RecurrenceType = "None" | "Daily" | "Weekly" | "Biweekly" | "Monthly";

export interface ScheduleDto {
    id: string;
    name: string;
    description?: string;
    teamId: string;
    teamName?: string;
    timezone: string;
    currentOnCallUser?: string;
    rotationCount: number;
    createdAt: string;
}

export interface ScheduleDetailDto extends ScheduleDto {
    rotations: ScheduleRotationDto[];
    overrides: OnCallOverrideDto[];
}

export interface ScheduleRotationDto {
    id: string;
    scheduleId: string;
    userId: string;
    userName?: string;
    userInitials?: string;
    isPrimary: boolean;
    order: number;
    handoverStartLocal?: string;
    shiftLengthMinutes: number;
    recurrenceType?: RecurrenceType;
    /**
     * Exact period between handovers in days. Overrides recurrenceType when set.
     * Used to express cadences that don't fit the enum, e.g. 2 members rotating
     * daily (2-day cycle) or 3 members weekly (21-day cycle).
     */
    recurrenceIntervalDays?: number;
    recurrenceEndDate?: string;
    startUtc?: string;
    endUtc?: string;
}

export interface OnCallOverrideDto {
    id: string;
    scheduleId: string;
    scheduleName: string;
    overrideUserId: string;
    overrideUserName?: string;
    overrideUserInitials?: string;
    originalUserId?: string;
    originalUserName?: string;
    startUtc: string;
    endUtc: string;
    reason?: string;
    isActive: boolean;
}

export interface OnCallStatusDto {
    scheduleId: string;
    scheduleName: string;
    primaryUserId?: string;
    primaryUserName?: string;
    primaryUserInitials?: string;
    secondaryUserId?: string;
    secondaryUserName?: string;
    secondaryUserInitials?: string;
    nextRotation?: string;
    nextOnCallUserName?: string;
}

export interface CreateScheduleRequest {
    name: string;
    description?: string;
    teamId: string;
    timezone: string;
}

export interface UpdateScheduleRequest {
    name?: string;
    description?: string;
    timezone?: string;
    isActive?: boolean;
    teamId?: string;
}

export interface CreateRotationRequest {
    userId: string;
    handoverStartLocal: string;
    shiftLengthMinutes: number;
    isPrimary?: boolean;
    order?: number;
    recurrenceType?: RecurrenceType;
    recurrenceIntervalDays?: number;
    recurrenceEndDate?: string;
}

export interface UpdateRotationRequest {
    handoverStartLocal?: string;
    shiftLengthMinutes?: number;
    isPrimary?: boolean;
    order?: number;
    recurrenceType?: RecurrenceType;
    recurrenceIntervalDays?: number | null;
    recurrenceEndDate?: string;
}

export interface CreateOverrideRequest {
    scheduleId: string;
    overrideUserId: string;
    originalUserId?: string;
    startUtc: string;
    endUtc: string;
    reason?: string;
}

export interface UpdateOverrideRequest {
    overrideUserId?: string;
    startUtc?: string;
    endUtc?: string;
    reason?: string;
}
