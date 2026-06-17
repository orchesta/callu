export interface RunbookDto {
    id: string;
    title: string;
    description?: string;
    content: string;
    serviceId?: string;
    serviceName?: string;
    authorId: string;
    tags: string[];
    lastUsedAt?: string;
    usageCount: number;
    createdAt: string;
    updatedAt?: string;
}

export interface CreateRunbookRequest {
    title: string;
    description?: string;
    content: string;
    serviceId?: string;
    tags: string[];
}

export interface UpdateRunbookRequest {
    title: string;
    description?: string;
    content: string;
    serviceId?: string;
    tags: string[];
}
