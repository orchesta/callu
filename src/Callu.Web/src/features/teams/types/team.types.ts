/**
 * Teams feature types
 * Mirrors BE DTOs from Callu.Shared.Models.Teams
 */

/** Team for list views (BE: TeamDto) */
export interface TeamDto {
    id: string;
    name: string;
    description?: string;
    icon?: string;
    color?: string;
    memberCount: number;
    serviceCount: number;
    createdAt: string;
}

/** Team detail with members (BE: TeamDetailDto extends TeamDto) */
export interface TeamDetailDto extends TeamDto {
    members: TeamMemberDto[];
}

/** Team member (BE: TeamMemberDto) */
export interface TeamMemberDto {
    id: string;
    userId: string;
    name: string;
    email: string;
    initials?: string;
    role: string;
    joinedAt: string;
}

/** BE: CreateTeamRequest */
export interface CreateTeamRequest {
    name: string;
    description?: string;
    icon?: string;
    color?: string;
}

/** BE: UpdateTeamRequest */
export interface UpdateTeamRequest {
    name?: string;
    description?: string;
    icon?: string;
    color?: string;
}

/** BE: AddMemberRequest (inline record in controller) */
export interface AddMemberRequest {
    userId: string;
    role: string;
}

export interface TeamColor {
    value: string;
    label: string;
}

