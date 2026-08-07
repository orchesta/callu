/**
 * Audit Log API module — connects to AuditLogsController.
 *   GET /api/v1/audit-logs?entityName=&entityId=&count=  (policy: CanViewAuditLog)
 */

import { apiClient } from '@/shared/api';
import type { QueryParams } from '@/shared/api';
import { API_URL } from '@/shared/config';
import { authService } from '@/shared/auth/auth.service';
import type { PagedResult } from '@/shared/types/common.types';
import type { AuditChainVerdict, AuditLogEntry, AuditLogFilters, AuditLogSearchFilter } from '../types/audit-log.types';

const BASE = '/api/v1/audit-logs';

export const auditLogApi = {
  /** GET /audit-logs — recent entries (newest first), optionally filtered by entity. */
  getAll: (filters: AuditLogFilters = {}) =>
    apiClient.get<AuditLogEntry[]>(BASE, {
      params: {
        entityName: filters.entityName,
        entityId: filters.entityId,
        count: filters.count,
      },
    }),

  /** GET /audit-logs/search — the filtered, paged trail. */
  search: (filter: AuditLogSearchFilter) =>
    apiClient.get<PagedResult<AuditLogEntry>>(`${BASE}/search`, {
      params: filter as QueryParams,
    }),

  /** POST /audit-logs/verify — replay the tamper-evidence chain. */
  verify: () => apiClient.post<AuditChainVerdict>(`${BASE}/verify`),
};

/**
 * Downloads the filtered trail.
 */
// Not an <a href>: a browser navigation carries no Authorization header, so the link answered 401.
// The response is streamed by the server but has to be held here to hand the browser a file.
export async function downloadAuditExport(
    format: 'csv' | 'jsonl',
    params: URLSearchParams,
): Promise<void> {
    params.set('format', format);
    const url = `${API_URL}${BASE}/export?${params.toString()}`;

    let response = await fetch(url, { headers: authHeader() });

    // The access token is short-lived and an export is the kind of thing done after a while on the
    // page; renew once rather than making the operator retry.
    if (response.status === 401 && (await authService.refreshAccessToken())) {
        response = await fetch(url, { headers: authHeader() });
    }

    if (!response.ok) {
        throw new Error(`Export failed with HTTP ${response.status}`);
    }

    saveAs(await response.blob(), fileName(response, format));
}

/** The server's name, but never without the extension — a file the operating system cannot open is
 *  not a successful download. */
function fileName(response: Response, format: 'csv' | 'jsonl'): string {
    const fromServer = fileNameFrom(response);
    const stamp = new Date().toISOString().slice(0, 19).replace(/[:T-]/g, '');
    const name = fromServer && fromServer.trim() ? fromServer.trim() : `audit-${stamp}`;

    return name.toLowerCase().endsWith(`.${format}`) ? name : `${name}.${format}`;
}

function authHeader(): HeadersInit {
    const token = authService.getAccessToken();
    return token ? { Authorization: `Bearer ${token}` } : {};
}

function fileNameFrom(response: Response): string | null {
    const disposition = response.headers.get('content-disposition');
    if (!disposition) return null;

    // filename*= (RFC 5987) is checked first: it is the encoded form and it sits after the plain
    // one, so a regex for `filename=` would otherwise capture its `*=UTF-8''…` value as the name.
    const encoded = disposition.match(/filename\*=(?:UTF-8'')?([^;]+)/i)?.[1];
    if (encoded) return safeDecode(encoded.trim().replace(/^"|"$/g, ''));

    return disposition.match(/filename="([^"]+)"/i)?.[1]
        ?? disposition.match(/filename=([^;]+)/i)?.[1]?.trim()
        ?? null;
}

function safeDecode(value: string): string {
    try { return decodeURIComponent(value); } catch { return value; }
}

function saveAs(blob: Blob, fileName: string): void {
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(objectUrl);
}
