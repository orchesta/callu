interface PageHeaderProps {
    title: string;
    subtitle?: string;
    action?: React.ReactNode;
}

export function PageHeader({ title, subtitle, action }: PageHeaderProps) {
    return (
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
            <div>
                <h1 className="text-3xl font-semibold">{title}</h1>
                {subtitle && (
                    <p className="text-sm text-muted-foreground mt-1">{subtitle}</p>
                )}
            </div>
            {action}
        </div>
    );
}
