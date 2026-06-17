/**
 * Teams React Query hooks.
 *
 * Provides type-safe queries and mutations for all team operations.
 * Uses teamQueries + useQuery / useApiMutation (ApiResponse unwrap).
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { teamApi } from '../api/team.api';
import type {
    CreateTeamRequest,
    UpdateTeamRequest,
    AddMemberRequest,
} from '../types/team.types';

export const teamKeys = {
    all: ['teams'] as const,
    lists: () => [...teamKeys.all, 'list'] as const,
    details: () => [...teamKeys.all, 'detail'] as const,
    detail: (id: string) => [...teamKeys.details(), id] as const,
};

export const teamQueries = {
    list: () => apiQueryOptions(teamKeys.lists(), () => teamApi.getAll(), { staleTime: 2 * 60_000 }),
    detail: (id: string) => apiQueryOptions(teamKeys.detail(id), () => teamApi.getById(id), { enabled: !!id }),
};

/** All teams list */
export function useTeams() {
    return useQuery(teamQueries.list());
}

/** Single team detail with members */
export function useTeam(id: string) {
    return useQuery(teamQueries.detail(id));
}

export function useCreateTeam() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateTeamRequest) => teamApi.create(data),
        {
            successMessage: 'Team created',
            onSuccess: () => {
                qc.invalidateQueries({ queryKey: teamKeys.lists() });
            },
        },
    );
}

export function useUpdateTeam() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateTeamRequest) =>
            teamApi.update(id, data),
        {
            successMessage: 'Team updated',
            onSuccess: () => {
                qc.invalidateQueries({ queryKey: teamKeys.all });
            },
        },
    );
}

export function useDeleteTeam() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => teamApi.delete(id),
        {
            successMessage: 'Team deleted',
            onSuccess: () => {
                qc.invalidateQueries({ queryKey: teamKeys.lists() });
            },
        },
    );
}

export function useAddMember() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ teamId, ...data }: { teamId: string } & AddMemberRequest) =>
            teamApi.addMember(teamId, data),
        {
            successMessage: 'Member added',
            onSuccess: (_, { teamId }) => {
                qc.invalidateQueries({ queryKey: teamKeys.detail(teamId) });
                qc.invalidateQueries({ queryKey: teamKeys.lists() });
            },
        },
    );
}

export function useRemoveMember() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ teamId, memberId }: { teamId: string; memberId: string }) =>
            teamApi.removeMember(teamId, memberId),
        {
            successMessage: 'Member removed',
            onSuccess: (_, { teamId }) => {
                qc.invalidateQueries({ queryKey: teamKeys.detail(teamId) });
                qc.invalidateQueries({ queryKey: teamKeys.lists() });
            },
        },
    );
}

export function useUpdateMemberRole() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ teamId, memberId, role }: { teamId: string; memberId: string; role: string }) =>
            teamApi.updateMemberRole(teamId, memberId, role),
        {
            successMessage: 'Role updated',
            onSuccess: (_, { teamId }) => {
                qc.invalidateQueries({ queryKey: teamKeys.detail(teamId) });
            },
        },
    );
}
