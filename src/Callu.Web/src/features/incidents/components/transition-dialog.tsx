import { useEffect, useId, useState } from "react";
import { Button } from "@/shared/components/ui/button";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { t } from "@/shared/locales/i18n";
import { MAX_REASON_LENGTH, RESOLUTION_TYPES, type ResolutionType } from "../api/incident.api";

export type TransitionMode = "resolve" | "close" | "reopen";

export interface TransitionInput {
  resolutionType?: ResolutionType;
  reason?: string;
}

interface Props {
  mode: TransitionMode | null;
  pending: boolean;
  onCancel: () => void;
  onConfirm: (input: TransitionInput) => void;
}

/** Asks for what the audit record needs: how an incident was resolved, why it was closed or reopened.
 * A reason is optional on resolve and required on close and reopen, as the incident-management profile reads them. */
export function TransitionDialog({ mode, pending, onCancel, onConfirm }: Props) {
  const [resolutionType, setResolutionType] = useState<ResolutionType | "">("");
  const [reason, setReason] = useState("");
  const typeId = useId();
  const reasonId = useId();

  // A fresh dialog for each action: a reason typed for one incident must not ride along to the next.
  useEffect(() => {
    setResolutionType("");
    setReason("");
  }, [mode]);

  if (mode === null) return null;

  const trimmed = reason.trim();
  const tooLong = trimmed.length > MAX_REASON_LENGTH;
  const ready = mode === "resolve" ? resolutionType !== "" : trimmed.length > 0;

  const confirm = () =>
    onConfirm(
      mode === "resolve"
        ? { resolutionType: resolutionType as ResolutionType, reason: trimmed || undefined }
        : { reason: trimmed },
    );

  return (
    <Dialog open onOpenChange={(open) => { if (!open && !pending) onCancel(); }}>
      <DialogContent className="bg-card border-border sm:max-w-[480px]">
        <DialogHeader>
          <DialogTitle style={{ fontSize: "1.25rem", fontWeight: 600 }}>
            {t(`incidents.transition.${mode}.title`)}
          </DialogTitle>
          <DialogDescription>{t(`incidents.transition.${mode}.description`)}</DialogDescription>
        </DialogHeader>

        {mode === "resolve" && (
          <div>
            <label htmlFor={typeId} className="text-sm font-medium">
              {t("incidents.transition.resolutionType")}
            </label>
            <select
              id={typeId}
              className="w-full mt-1 p-2.5 rounded-lg bg-input-background border border-border text-sm"
              value={resolutionType}
              onChange={(e) => setResolutionType(e.target.value as ResolutionType | "")}
            >
              <option value="">{t("incidents.transition.resolutionTypeChoose")}</option>
              {RESOLUTION_TYPES.map((type) => (
                <option key={type} value={type}>{t(`incidents.resolutionType.${type}`)}</option>
              ))}
            </select>
          </div>
        )}

        <div>
          <label htmlFor={reasonId} className="text-sm font-medium">
            {t(mode === "resolve" ? "incidents.transition.reasonOptional" : "incidents.transition.reason")}
          </label>
          <Textarea id={reasonId} value={reason} onChange={(e) => setReason(e.target.value)} className="mt-1" />
          {tooLong && <p className="text-xs text-error-400 mt-1">{t("incidents.transition.reasonTooLong")}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onCancel} disabled={pending} className="bg-input-background">
            {t("common.cancel")}
          </Button>
          <Button
            onClick={confirm}
            disabled={pending || !ready || tooLong}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {t(`incidents.transition.${mode}.confirm`)}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
