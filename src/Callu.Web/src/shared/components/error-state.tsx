import { AlertCircle } from "lucide-react";
import { Button } from "./ui/button";
import { t } from "@/shared/locales/i18n";

interface ErrorStateProps {
  title?: string;
  message?: string;
  onRetry?: () => void;
  className?: string;
}

export function ErrorState({
  title,
  message,
  onRetry,
  className = "",
}: ErrorStateProps) {
  const resolvedTitle = title ?? t("shared.errorState.defaultTitle");
  const resolvedMessage = message ?? t("shared.errorState.defaultMessage");

  return (
    <div className={`flex items-center justify-center min-h-[40vh] ${className}`}>
      <div className="text-center">
        <div className="flex justify-center mb-4">
          <div className="w-16 h-16 rounded-full bg-error-500/10 flex items-center justify-center">
            <AlertCircle className="w-8 h-8 text-error-500" />
          </div>
        </div>
        <p className="text-lg font-semibold mb-2">{resolvedTitle}</p>
        <p className="text-sm text-muted-foreground">{resolvedMessage}</p>
        {onRetry && (
          <Button variant="outline" onClick={onRetry} className="mt-4">
            {t("common.tryAgain")}
          </Button>
        )}
      </div>
    </div>
  );
}
