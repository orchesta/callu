/**
 * Teams API module — connects to TeamsController (7 endpoints).
 *
 * Endpoints:
 *   GET    /api/v1/teams                          → TeamDto[]
 *   GET    /api/v1/teams/:id                       → TeamDetailDto
 *   POST   /api/v1/teams                          → TeamDto (CreatedAtAction)
 *   PUT    /api/v1/teams/:id                       → void (204)
 *   DELETE /api/v1/teams/:id                       → void (204)
 *   POST   /api/v1/teams/:id/members               → { message }
 *   DELETE /api/v1/teams/:teamId/members/:memberId  → void (204)
 */

import { apiClient } from '@/shared/api';
import type {
    TeamDto,
    TeamDetailDto,
    CreateTeamRequest,
    UpdateTeamRequest,
    AddMemberRequest,
} from '../types/team.types';

const BASE = '/api/v1/teams';

export const teamApi = {
    /** Get all teams */
    getAll: () =>
        apiClient.get<TeamDto[]>(BASE),

    /** Get team detail with members */
    getById: (id: string) =>
        apiClient.get<TeamDetailDto>(`${BASE}/${id}`),

    /** Create a new team */
    create: (data: CreateTeamRequest) =>
        apiClient.post<TeamDto>(BASE, data),

    /** Update a team */
    update: (id: string, data: UpdateTeamRequest) =>
        apiClient.put<void>(`${BASE}/${id}`, data),

    /** Delete a team */
    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    /** Add a member to a team */
    addMember: (teamId: string, data: AddMemberRequest) =>
        apiClient.post<{ message: string }>(`${BASE}/${teamId}/members`, data),

    /** Remove a member from a team */
    removeMember: (teamId: string, memberId: string) =>
        apiClient.delete<void>(`${BASE}/${teamId}/members/${memberId}`),

    /** Update a member's role */
    updateMemberRole: (teamId: string, memberId: string, role: string) =>
        apiClient.put<void>(`${BASE}/${teamId}/members/${memberId}/role`, { role }),
};
