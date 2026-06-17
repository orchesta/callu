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
    lastNotifiedAt?: string;
    notificationCount: number;
    createdAt: string;
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

/** Channel type definition returned by GET /notification-channels/types */
export interface ChannelTypeDefinition {
    value: string;
    label: string;
    icon: string;
    description: string;
    fields: ChannelTypeField[];
}
