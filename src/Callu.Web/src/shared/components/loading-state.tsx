import { Loader2 } from "lucide-react";
import { t } from "@/shared/locales/i18n";

interface LoadingStateProps {
    message?: string;
    className?: string;
    size?: "sm" | "md";
}

export function LoadingState({
    message = t("common.loading"),
    className = "",
    size = "md",
}: LoadingStateProps) {
    if (size === "sm") {
        return (
            <div role="status" aria-busy="true" className={`flex items-center justify-center gap-2 py-4 ${className}`}>
                <Loader2 className="w-5 h-5 animate-spin text-brand-500" aria-hidden="true" />
                <p className="text-sm text-muted-foreground">{message}</p>
            </div>
        );
    }

    return (
        <div role="status" aria-busy="true" className={`flex items-center justify-center min-h-[40vh] ${className}`}>
            <div className="text-center">
                <Loader2 className="w-8 h-8 animate-spin text-brand-500 mx-auto mb-4" aria-hidden="true" />
                <p className="text-sm text-muted-foreground">{message}</p>
            </div>
        </div>
    );
}
