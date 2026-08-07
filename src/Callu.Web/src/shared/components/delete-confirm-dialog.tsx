import { AlertCircle, Trash2 } from "lucide-react";
import { Button } from "./ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "./ui/dialog";
import { t } from "@/shared/locales/i18n";

interface DeleteConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title?: string;
  message: string;
  warning?: string;
  onConfirm: () => void;
  isLoading?: boolean;
  confirmLabel?: string;
  cancelLabel?: string;
}

export function DeleteConfirmDialog({
  open,
  onOpenChange,
  title,
  message,
  warning,
  onConfirm,
  isLoading = false,
  confirmLabel,
  cancelLabel,
}: DeleteConfirmDialogProps) {
  const resolvedTitle = title ?? t("shared.deleteDialog.defaultTitle");
  const resolvedConfirm = confirmLabel ?? t("common.delete");
  const resolvedCancel = cancelLabel ?? t("common.cancel");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="bg-card border-border sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle className="text-2xl font-semibold">{resolvedTitle}</DialogTitle>
        </DialogHeader>
        <div className="py-4">
          <div className="flex gap-3 mb-4">
            <div className="w-10 h-10 rounded-full bg-error-500/10 flex items-center justify-center flex-shrink-0">
              <AlertCircle className="w-5 h-5 text-error-500" />
            </div>
            <div>
              <p className="text-sm mb-2">{message}</p>
              {warning && <p className="text-[0.8125rem] text-muted-foreground">{warning}</p>}
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={isLoading}
            className="bg-input-background"
          >
            {resolvedCancel}
          </Button>
          <Button
            onClick={onConfirm}
            disabled={isLoading}
            className="bg-error-500 hover:bg-error-600 text-white"
          >
            {isLoading ? (
              <>
                <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                {t("shared.deleteDialog.deleting")}
              </>
            ) : (
              <>
                <Trash2 className="w-4 h-4 mr-2" />
                {resolvedConfirm}
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
