import { motion, AnimatePresence } from "motion/react";
import { Trash2 } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import type { UserDto } from "../types/user.types";
import { getUserFullName } from "../utils/user-display";

export function DeleteConfirmModal({
  isOpen,
  onClose,
  user,
  onConfirm,
  isPending,
}: {
  isOpen: boolean;
  onClose: () => void;
  user: UserDto | null;
  onConfirm: () => void;
  isPending: boolean;
}) {
  if (!user) return null;

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm"
          />
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.95 }}
            className="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border border-red-500/20 bg-gray-900 p-6 shadow-2xl"
          >
            <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-red-500/20">
              <Trash2 className="h-6 w-6 text-red-400" />
            </div>
            <h2 className="text-2xl font-bold text-white">{t("users.removeUserConfirm")}</h2>
            <p className="mt-2 text-gray-400">
              {t("users.removeUserMessage")} <strong className="text-white">{getUserFullName(user)}</strong>
            </p>

            <div className="mt-6 flex items-center gap-3">
              <button
                type="button"
                onClick={onClose}
                className="flex-1 rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 font-medium text-white transition-colors hover:bg-white/10"
              >
                {t("common.cancel")}
              </button>
              <button
                onClick={onConfirm}
                disabled={isPending}
                className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-red-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-red-600 disabled:opacity-50"
              >
                {isPending ? (
                  <>
                    <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                    {t("users.removing")}
                  </>
                ) : (
                  <>
                    <Trash2 className="h-4 w-4" />
                    {t("users.removeUser")}
                  </>
                )}
              </button>
            </div>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}