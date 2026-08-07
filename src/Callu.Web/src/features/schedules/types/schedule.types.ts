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
    /** Exact days between handovers, overriding recurrenceType, for cadences the enum cannot express. */
    recurrenceIntervalDays?: number;
    /** Calendar days the member owns per cycle: one occurrence per owned day when set, a single
     * occurrence per cycle when null. */
    ownershipDays?: number;
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

/** BE: CoverageGap — uncovered Instant window on the primary rota. */
export interface CoverageGap {
    start: string;
    end: string;
}

/** BE: RotationCoverageResult — primary-only coverage over the next N days. */
export interface RotationCoverageResult {
    hasFullCoverage: boolean;
    gapHours: number;
    coveragePercent: number;
    gaps: CoverageGap[];
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
    /** 1..31 when supplied. Omit for a single block per cycle (legacy behaviour). */
    ownershipDays?: number;
    recurrenceEndDate?: string;
}

export interface UpdateRotationRequest {
    handoverStartLocal?: string;
    shiftLengthMinutes?: number;
    isPrimary?: boolean;
    order?: number;
    recurrenceType?: RecurrenceType;
    recurrenceIntervalDays?: number | null;
    /**
     * 1..31. Omitted fields are left untouched by the API, so a stored value cannot be
     * cleared — send 1 to get back to single-block behaviour (equivalent to the legacy null).
     */
    ownershipDays?: number;
    recurrenceEndDate?: string;
}

/** One rotation of a schedule plan. Not a patch: every field is written as given. */
export interface SchedulePlanRotation {
    /** Existing rotation to update. Omit to create a new one. */
    id?: string;
    userId: string;
    handoverStartLocal: string;
    shiftLengthMinutes: number;
    isPrimary: boolean;
    order: number;
    recurrenceType?: RecurrenceType;
    recurrenceIntervalDays?: number;
    ownershipDays?: number;
    recurrenceEndDate?: string;
}

/** A schedule's whole on-call plan, applied in one transaction with a single rematerialize at the end.
 * `rotations` fully replaces the stored list; omitting it leaves the stored rotations untouched. */
export interface SaveSchedulePlanRequest {
    name?: string;
    description?: string;
    timezone?: string;
    teamId?: string;
    rotations?: SchedulePlanRotation[];
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
