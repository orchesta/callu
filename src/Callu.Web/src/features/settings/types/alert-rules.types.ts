export interface AlertRuleConditionDto {
    field: string;
    operator: string;
    value: string;
}

export interface AlertRuleActionDto {
    type: string;
    target?: string;
    value?: string;
}

export interface AlertRuleDto {
    id: string;
    name: string;
    description?: string;
    isEnabled: boolean;
    priority: number;
    conditions: AlertRuleConditionDto[];
    actions: AlertRuleActionDto[];
    serviceId?: string;
    serviceName?: string;
    teamId?: string;
    teamName?: string;
    triggerCount: number;
    lastTriggeredAt?: string;
    createdAt: string;
    updatedAt?: string;
}

export interface CreateAlertRuleRequest {
    name: string;
    description?: string;
    isEnabled: boolean;
    priority: number;
    conditions: AlertRuleConditionDto[];
    actions: AlertRuleActionDto[];
    serviceId?: string;
    teamId?: string;
}

export interface UpdateAlertRuleRequest {
    name: string;
    description?: string;
    isEnabled: boolean;
    priority: number;
    conditions: AlertRuleConditionDto[];
    actions: AlertRuleActionDto[];
    serviceId?: string;
    teamId?: string;
}

/** Metadata returned by GET /alert-rules/metadata */
export interface AlertRuleMetadata {
    conditionFields: Array<{ value: string; label: string }>;
    conditionOperators: Array<{ value: string; label: string }>;
    actionTypes: Array<{ value: string; label: string }>;
    severityValues: string[];
}
