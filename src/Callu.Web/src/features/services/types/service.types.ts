/**
 * Service Domain Types — aligned with BE Shared DTOs
 */

export enum ServiceStatus {
  Operational = 'Operational',
  DegradedPerformance = 'DegradedPerformance',
  PartialOutage = 'PartialOutage',
  MajorOutage = 'MajorOutage',
  UnderMaintenance = 'UnderMaintenance',
}

export enum ServiceType {
  Api = 'Api',
  Website = 'Website',
  Database = 'Database',
  Queue = 'Queue',
  Cache = 'Cache',
  Cdn = 'Cdn',
  Storage = 'Storage',
  Email = 'Email',
  ThirdParty = 'ThirdParty',
  Other = 'Other',
}

export enum DependencyType {
  Upstream = 'Upstream',
  Downstream = 'Downstream',
  Bidirectional = 'Bidirectional',
}

export enum DependencyCriticality {
  Critical = 'Critical',
  High = 'High',
  Medium = 'Medium',
  Low = 'Low',
  Optional = 'Optional',
}

/** Mirrors Callu.Shared.Models.Services.ServiceDto */
export interface ServiceDto {
  id: string;
  name: string;
  description?: string;
  type: string;
  status: string;
  environment?: string;
  uptime: number;
  color?: string;
  icon?: string;
  isPublic: boolean;
  displayOrder: number;
  teamId?: string;
  teamName?: string;
  incidentCount: number;
  webhookEnabled?: boolean;
  webhookTemplateId?: string;
  createdAt: string;

  ackEnabled: boolean;
  ackUrl?: string;
  ackHttpMethod: string;
  ackContentType: string;
  ackHeaders?: string;
  ackPayloadTemplate?: string;
}

/** Lightweight service for list views (BE returns full ServiceDto) */
export interface ServiceListDto {
  id: string;
  name: string;
  description?: string;
  type: string;
  status: string;
  environment?: string;
  uptime: number;
  teamName?: string;
  incidentCount: number;
  isPublic: boolean;
  displayOrder: number;
  createdAt: string;
}

/** Mirrors Callu.Shared.Models.Services.ServiceDependencyDto */
export interface ServiceDependencyDto {
  id: string;
  serviceId: string;
  serviceName: string;
  dependsOnServiceId: string;
  dependsOnServiceName: string;
  type: string;
  criticality: string;
  description?: string;
}

/** Mirrors Callu.Shared.Models.Services.CreateServiceRequest */
export interface CreateServiceRequest {
  name: string;
  description?: string;
  type?: string;
  environment?: string;
  color?: string;
  icon?: string;
  isPublic?: boolean;
  teamId?: string;
}

/** Mirrors Callu.Shared.Models.Services.UpdateServiceRequest */
export interface UpdateServiceRequest {
  name?: string;
  description?: string;
  type?: string;
  environment?: string;
  status?: string;
  color?: string;
  icon?: string;
  isPublic?: boolean;
  teamId?: string;

  ackEnabled?: boolean;
  ackUrl?: string;
  ackHttpMethod?: string;
  ackContentType?: string;
  ackHeaders?: string;
  ackPayloadTemplate?: string;
}

/** Mirrors Callu.Shared.Models.Services.CreateServiceDependencyRequest */
export interface CreateServiceDependencyRequest {
  dependsOnServiceId: string;
  type: string;
  criticality: string;
  description?: string;
  cascadeStatus?: boolean;
}

export interface WebhookTemplate {
  id: string;
  serviceId: string;
  name: string;
  description: string;
  samplePayload: string;
  fieldMappings: WebhookFieldMapping;
  stateMapping: WebhookStateMapping;
  severityMappings: WebhookSeverityMapping[];
  isActive: boolean;
  createdAt: Date;
  updatedAt: Date;
}

export interface WebhookFieldMapping {
  title: string;
  description: string;
  severity?: string;
  externalId?: string;
}

export interface WebhookStateMapping {
  stateField: string;
  openValue: string;
  resolvedValue: string;
}

export interface WebhookSeverityMapping {
  sourceValue: string;
  targetSeverity: 'critical' | 'high' | 'medium' | 'low';
}

export interface WebhookCapture {
  id: string;
  serviceId: string;
  method: string;
  statusCode: number;
  headers: Record<string, string>;
  body: string;
  timestamp: Date;
  reviewed: boolean;
}

export interface CreateWebhookTemplateDto {
  name: string;
  description: string;
  samplePayload: string;
  fieldMappings: WebhookFieldMapping;
  stateMapping: WebhookStateMapping;
  severityMappings: WebhookSeverityMapping[];
}
