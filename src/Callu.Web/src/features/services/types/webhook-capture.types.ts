/**
 * Webhook Capture Types — mirrors WebhookCaptureDto from
 * Callu.Shared.Models.Webhooks
 */

export interface WebhookCaptureDto {
    id: string;
    serviceId: string;
    capturedAt: string;
    method: 'GET' | 'POST' | 'PUT' | 'PATCH';
    contentType?: string;
    sourceIp?: string;
    headers: string;
    body: string;
    bodySize: number;
    status: string;
}
