import { useState } from "react";
import { Button } from "@/shared/components/ui/button";
import { Card } from "@/shared/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { Play } from "lucide-react";
import { useServiceActions, useExecuteServiceAction } from "../hooks/use-incidents";
import type { ServiceActionDto } from "../types/incident.types";
import { hasPermission, PERMISSIONS } from "@/shared/auth";
import { useAuth } from "@/shared/auth/auth.context";
import { t } from "@/shared/locales/i18n";

export function ServiceActionsCard({ incidentId, serviceId }: { incidentId: string; serviceId?: string }) {
  const { user } = useAuth();
  const canExecute = hasPermission(user?.role, PERMISSIONS.ExecuteServiceActions);
  const { data: actions = [] } = useServiceActions(canExecute ? serviceId ?? "" : "");
  const executeAction = useExecuteServiceAction(incidentId);
  const [confirmAction, setConfirmAction] = useState<ServiceActionDto | null>(null);

  const enabledActions = actions
    .filter((a) => a.isEnabled)
    .sort((a, b) => a.displayOrder - b.displayOrder);

  if (!serviceId || !canExecute || enabledActions.length === 0) return null;

  const result = executeAction.data;
  const errorMessage =
    executeAction.error?.statusCode === 409
      ? t("serviceActions.throttled")
      : executeAction.error?.message;
  const hasResult = !!result || !!errorMessage;

  const handleRun = (action: ServiceActionDto) => {
    executeAction.reset();
    setConfirmAction(action);
  };

  const handleClose = () => {
    if (!executeAction.isPending) setConfirmAction(null);
  };

  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
      <h3 className="text-lg font-semibold mb-4 flex items-center gap-2">
        <Play className="w-5 h-5 text-brand-500" />
        {t("serviceActions.incidentCardTitle")}
      </h3>
      <div className="space-y-2">
        {enabledActions.map((action) => (
          <div
            key={action.id}
            className="flex items-center justify-between gap-3 p-3 rounded-lg bg-muted/5 border border-border"
          >
            <div className="flex-1 min-w-0">
              <p className="text-sm font-medium truncate">{action.name}</p>
              {action.description && (
                <p className="text-xs text-muted-foreground truncate">{action.description}</p>
              )}
            </div>
            <Button
              size="sm"
              variant="outline"
              className="bg-input-background flex-shrink-0"
              disabled={executeAction.isPending}
              onClick={() => handleRun(action)}
            >
              {t("serviceActions.runButton")}
            </Button>
          </div>
        ))}
      </div>

      <Dialog open={!!confirmAction} onOpenChange={(open) => { if (!open) handleClose(); }}>
        <DialogContent className="bg-card border-border sm:max-w-[480px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.25rem", fontWeight: 600 }}>
              {confirmAction?.name}
            </DialogTitle>
            <DialogDescription className="font-mono text-xs break-all">
              {confirmAction?.url}
            </DialogDescription>
          </DialogHeader>

          <p className="text-sm text-muted-foreground">{t("serviceActions.runConfirmText")}</p>

          {result && (
            <div className="space-y-1">
              <p
                className={`text-sm font-semibold ${
                  result.outcome === "succeeded" ? "text-success-500" : "text-error-400"
                }`}
              >
                {result.outcome === "succeeded"
                  ? t("serviceActions.resultSucceeded")
                  : t("serviceActions.resultFailed")}
              </p>
              {result.httpStatus != null && (
                <p className="text-xs text-muted-foreground">
                  {t("serviceActions.httpStatus")}: {result.httpStatus}
                </p>
              )}
              {result.error && <p className="text-xs text-error-400">{result.error}</p>}
            </div>
          )}
          {errorMessage && <p className="text-sm text-error-400">{errorMessage}</p>}

          <DialogFooter>
            {hasResult ? (
              <Button variant="outline" onClick={handleClose} className="bg-input-background">
                {t("common.close")}
              </Button>
            ) : (
              <>
                <Button
                  variant="outline"
                  onClick={handleClose}
                  disabled={executeAction.isPending}
                  className="bg-input-background"
                >
                  {t("common.cancel")}
                </Button>
                <Button
                  disabled={executeAction.isPending}
                  onClick={() => { if (confirmAction) executeAction.mutate(confirmAction.id); }}
                  className="bg-brand-500 hover:bg-brand-600 text-white"
                >
                  {executeAction.isPending ? t("serviceActions.running") : t("serviceActions.runButton")}
                </Button>
              </>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
