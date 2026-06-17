export interface PostmortemActionItemDto {
    description: string;
    assigneeId?: string;
    assigneeName?: string;
    isComplete: boolean;
    dueDate?: string;
}

export interface PostmortemDto {
    id: string;
    title: string;
    content: string;
    rootCause?: string;
    incidentId: string;
    incidentTitle?: string;
    authorId: string;
    status: string;
    publishedAt?: string;
    actionItems: PostmortemActionItemDto[];
    createdAt: string;
    updatedAt?: string;
}

export interface CreatePostmortemRequest {
    title: string;
    content: string;
    rootCause?: string;
    incidentId: string;
    actionItems: PostmortemActionItemDto[];
}

export interface UpdatePostmortemRequest {
    title: string;
    content: string;
    rootCause?: string;
    actionItems: PostmortemActionItemDto[];
}
