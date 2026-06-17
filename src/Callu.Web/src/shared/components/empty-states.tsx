import { LucideIcon, AlertCircle, Inbox } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { t } from "@/shared/locales/i18n";

interface EmptyStateProps {
  icon?: LucideIcon;
  title: string;
  description: string;
  action?:
    | {
        label: string;
        onClick: () => void;
        variant?: "default" | "outline" | "secondary";
      }
    | React.ReactNode;
  secondaryAction?: {
    label: string;
    onClick: () => void;
  };
  className?: string;
}

export function EmptyState({
  icon: Icon = Inbox,
  title,
  description,
  action,
  secondaryAction,
  className = "",
}: EmptyStateProps) {
  return (
    <Card className={`p-12 ${className}`}>
      <div className="flex flex-col items-center text-center max-w-md mx-auto">
        <div className="w-20 h-20 rounded-full bg-gray-100 dark:bg-gray-800 flex items-center justify-center mb-6">
          <Icon className="w-10 h-10 text-gray-400 dark:text-gray-600" />
        </div>

        <h3 className="text-xl font-semibold text-gray-900 dark:text-white mb-2">{title}</h3>
        <p className="text-gray-600 dark:text-gray-400 mb-6">{description}</p>

        {(action || secondaryAction) && (
          <div className="flex flex-col sm:flex-row gap-3 w-full sm:w-auto">
            {action && typeof action !== "object" || (action && "label" in (action as object)) ? (
              <Button
                onClick={(action as { onClick: () => void }).onClick}
                variant={(action as { variant?: "default" | "outline" | "secondary" }).variant || "default"}
                size="lg"
                className="sm:min-w-[140px]"
              >
                {(action as { label: string }).label}
              </Button>
            ) : (
              (action as React.ReactNode)
            )}
            {secondaryAction && (
              <Button
                onClick={secondaryAction.onClick}
                variant="outline"
                size="lg"
                className="sm:min-w-[140px]"
              >
                {secondaryAction.label}
              </Button>
            )}
          </div>
        )}
      </div>
    </Card>
  );
}

export function NoIncidentsEmptyState({ onCreateIncident }: { onCreateIncident?: () => void }) {
  return (
    <EmptyState
      icon={AlertCircle}
      title={t("shared.emptyStates.noIncidentsTitle")}
      description={t("shared.emptyStates.noIncidentsDesc")}
      action={
        onCreateIncident
          ? {
              label: t("shared.emptyStates.triggerTestIncident"),
              onClick: onCreateIncident,
              variant: "outline",
            }
          : undefined
      }
    />
  );
}
