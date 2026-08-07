import { useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Crown, Eye, Mail, Shield } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import { roleOptions, roleDescriptions } from "../utils/roles";

export function InviteUserModal({
  isOpen,
  onClose,
  onInvite,
  isPending,
}: {
  isOpen: boolean;
  onClose: () => void;
  onInvite: (email: string, role: string) => void;
  isPending: boolean;
}) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState("Member");

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (isPending) return;
    onInvite(email, role);
  };

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => !isPending && onClose()}
            className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm"
          />
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.95 }}
            className="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-white/10 bg-gray-900 p-6 shadow-2xl"
          >
            <h2 className="text-2xl font-bold text-white">{t("users.inviteNewUser")}</h2>
            <p className="mt-2 text-gray-400">{t("users.inviteDescription")}</p>

            <form onSubmit={handleSubmit} className="mt-6 space-y-4">
              <div>
                <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.emailAddress")}</label>
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder={t("users.emailInvitePlaceholder")}
                  required
                  className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                />
              </div>

              <div>
                <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.colRole")}</label>
                <div className="space-y-2">
                  {roleOptions.map((roleOption) => (
                    <label
                      key={roleOption}
                      className={`flex cursor-pointer items-start gap-3 rounded-lg border-2 p-4 transition-colors ${role === roleOption
                        ? "border-brand-500 bg-brand-500/10"
                        : "border-white/10 bg-white/5 hover:bg-white/10"
                        }`}
                    >
                      <input
                        type="radio"
                        name="role"
                        value={roleOption}
                        checked={role === roleOption}
                        onChange={(e) => setRole(e.target.value)}
                        className="mt-1"
                      />
                      <div className="flex-1">
                        <div className="flex items-center gap-2">
                          {roleOption === "Admin" && <Crown className="h-4 w-4 text-red-400" />}
                          {roleOption === "TeamLead" && <Shield className="h-4 w-4 text-purple-400" />}
                          {roleOption === "Member" && <Shield className="h-4 w-4 text-brand-400" />}
                          {roleOption === "Viewer" && <Eye className="h-4 w-4 text-gray-400" />}
                          <p className="font-medium text-white">{roleOption}</p>
                        </div>
                        <p className="mt-1 text-sm text-gray-400">{roleDescriptions[roleOption]}</p>
                      </div>
                    </label>
                  ))}
                </div>
              </div>

              <div className="flex items-center gap-3 pt-4">
                <button
                  type="button"
                  onClick={onClose}
                  className="flex-1 rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 font-medium text-white transition-colors hover:bg-white/10"
                >
                  {t("common.cancel")}
                </button>
                <button
                  type="submit"
                  disabled={isPending || !email}
                  className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-brand-600 disabled:opacity-50"
                >
                  {isPending ? (
                    <>
                      <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                      {t("users.sending")}
                    </>
                  ) : (
                    <>
                      <Mail className="h-4 w-4" />
                      {t("users.sendInvitation")}
                    </>
                  )}
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}
