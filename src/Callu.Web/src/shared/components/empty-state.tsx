interface EmptyStateProps {
    icon: React.ElementType;
    title: string;
    description?: string;
    action?: React.ReactNode;
    className?: string;
}

export function EmptyState({
    icon: Icon,
    title,
    description,
    action,
    className = "",
}: EmptyStateProps) {
    return (
        <div className={`text-center py-12 ${className}`}>
            <div className="flex justify-center mb-4">
                <div className="w-16 h-16 rounded-full bg-muted/20 flex items-center justify-center">
                    <Icon className="w-8 h-8 text-muted-foreground" />
                </div>
            </div>
            <p className="text-lg font-semibold mb-2">{title}</p>
            {description && (
                <p className="text-sm text-muted-foreground mb-6">{description}</p>
            )}
            {action}
        </div>
    );
}
