import { useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Check, Copy, MailWarning } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import { copyText } from "@/shared/utils/clipboard";

/** Shown when the account was created but the invitation email could not be sent. */
// The link is the only way the invitee can reach the product, so it has to be copyable rather
// than sitting in a toast that disappears.
export function InviteLinkDialog({
  email,
  link,
  onClose,
}: {
  email: string;
  link: string;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    if (!(await copyText(link))) return;

    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      >
        <motion.div
          className="w-full max-w-lg rounded-xl border border-border bg-card p-6"
          initial={{ opacity: 0, scale: 0.97 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.97 }}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="mb-3 flex items-start gap-3">
            <MailWarning className="mt-0.5 h-5 w-5 shrink-0 text-amber-400" />
            <div>
              <h2 className="text-base font-medium text-ink">{t("users.invitationNotEmailedTitle")}</h2>
              <p className="mt-1 text-sm text-dim">
                {t("users.invitationNotEmailedBody", { email })}
              </p>
            </div>
          </div>

          <div className="mb-4 flex items-stretch gap-2">
            <code className="flex-1 overflow-x-auto rounded-lg border border-border bg-input-background px-3 py-2 font-mono text-xs text-ink">
              {link}
            </code>
            <button
              type="button"
              onClick={copy}
              title={t("common.copy")}
              className="flex items-center gap-1.5 rounded-lg border border-border px-3 text-xs text-dim hover:text-ink"
            >
              {copied ? <Check className="h-4 w-4 text-emerald-400" /> : <Copy className="h-4 w-4" />}
              {copied ? t("common.copied") : t("common.copy")}
            </button>
          </div>

          <p className="mb-4 text-xs text-dim">{t("users.invitationNotEmailedHint")}</p>

          <div className="flex justify-end">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg bg-brand-500 px-4 py-2 text-sm font-medium text-white hover:bg-brand-400"
            >
              {t("common.done")}
            </button>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}
