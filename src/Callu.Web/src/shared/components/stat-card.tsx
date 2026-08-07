interface StatCardProps {
    label: string;
    value: string | number;
    color?: string;
    borderColor?: string;
}

export function StatCard({
    label,
    value,
    color,
    borderColor = "border-border",
}: StatCardProps) {
    return (
        <div className={`p-4 rounded-lg bg-card/80 backdrop-blur-sm border ${borderColor}`}>
            <p
                className="text-xs font-semibold tracking-wide"
                style={{ color: color || "#94A3B8" }}
            >
                {label}
            </p>
            <p
                className="text-2xl font-bold mt-2"
                style={color ? { color } : undefined}
            >
                {value}
            </p>
        </div>
    );
}
