import { apiClient } from '@/shared/api';
import type { BuildIdentity } from '../types/tracing.types';

const BASE = '/api/v1/diagnostics';

export const diagnosticsApi = {
  getBuild: () => apiClient.get<BuildIdentity>(`${BASE}/build`),
};
