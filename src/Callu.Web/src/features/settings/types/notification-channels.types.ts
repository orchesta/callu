/** One outbound attempt on a channel */
export interface NotificationChannelDeliveryDto {
    id: string;
    incidentId: string;
    eventKey: string;
    title: string;
    severity?: string;
    messageText: string;
    status: 'Succeeded' | 'Failed' | 'Retrying';
    httpStatus?: number;
    error?: string;
    attemptCount: number;
    attemptedAt: string;
    nextRetryAt?: string;
}

export interface PagedDeliveries {
    items: NotificationChannelDeliveryDto[];
    totalCount: number;
    page: number;
    pageSize: number;
    totalPages: number;
}

export interface NotificationChannelDto {
    id: string;
    name: string;
    channelType: string;
    configuration: Record<string, string>;
    isEnabled: boolean;
    minimumSeverity?: string;
    serviceFilter: string[];
    notifyOnIncidentCreated: boolean;
    notifyOnIncidentAcknowledged: boolean;
    notifyOnIncidentResolved: boolean;
    notifyOnIncidentClosed: boolean;
    notifyOnIncidentReopened: boolean;
    lastNotifiedAt?: string;
    notificationCount: number;
    createdAt: string;
    /** Outcome of the most recent attempt — unlike lastNotifiedAt, a failure updates this too. */
    lastDeliveryStatus?: 'Succeeded' | 'Failed' | 'Retrying';
    lastDeliveryAt?: string;
}

export interface CreateNotificationChannelRequest {
    name: string;
    channelType: string;
    configuration: Record<string, string>;
    minimumSeverity?: string;
    serviceFilter: string[];
    notifyOnIncidentCreated: boolean;
    notifyOnIncidentAcknowledged: boolean;
    notifyOnIncidentResolved: boolean;
    notifyOnIncidentClosed: boolean;
    notifyOnIncidentReopened: boolean;
}

export interface UpdateNotificationChannelRequest {
    name: string;
    configuration: Record<string, string>;
    isEnabled: boolean;
    minimumSeverity?: string;
    serviceFilter: string[];
    notifyOnIncidentCreated: boolean;
    notifyOnIncidentAcknowledged: boolean;
    notifyOnIncidentResolved: boolean;
    notifyOnIncidentClosed: boolean;
    notifyOnIncidentReopened: boolean;
}

/** Single configurable field for a channel type (GET /notification-channels/types) */
export interface ChannelTypeField {
    key: string;
    label: string;
    input: 'text' | 'url' | 'password' | 'email' | 'select';
    required: boolean;
    placeholder?: string;
    helpUrl?: string;
    options?: { value: string; label: string }[];
}

/** A warning the backend attaches to a channel type (e.g. a transport being retired upstream) */
export interface ChannelTypeNotice {
    level: 'warning' | 'info';
    text: string;
}

/** Channel type definition returned by GET /notification-channels/types */
export interface ChannelTypeDefinition {
    value: string;
    label: string;
    icon: string;
    description: string;
    /** Pretty-printed JSON of exactly what this type sends; produced by the code that sends it. */
    samplePayload?: string;
    notice?: ChannelTypeNotice;
    fields: ChannelTypeField[];
}
