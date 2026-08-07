import { useState, useEffect } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Bell, Mail, MessageSquare, PhoneCall, Shield } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import {
  useUserNotificationPreferences,
  useUpdateUserNotificationPreferences,
} from "../hooks/use-users";
import type { UserDto, AdminUpdateUserRequest } from "../types/user.types";
import { getAvatarColor, getUserInitials, getUserFullName } from "../utils/user-display";
import { roleOptions } from "../utils/roles";

export function EditUserModal({
  isOpen,
  onClose,
  user,
  onChangeRole,
  onUpdateUser,
  isSaving,
}: {
  isOpen: boolean;
  onClose: () => void;
  user: UserDto;
  onChangeRole: (userId: string, newRole: string) => void;
  onUpdateUser: (userId: string, data: AdminUpdateUserRequest) => void;
  isSaving: boolean;
}) {
  const [firstName, setFirstName] = useState(user.firstName ?? "");
  const [lastName, setLastName] = useState(user.lastName ?? "");
  const [phoneNumber, setPhoneNumber] = useState(user.phoneNumber ?? "");
  const [role, setRole] = useState(user.role);

  const { data: prefs } = useUserNotificationPreferences(user.id, isOpen);
  const updatePrefs = useUpdateUserNotificationPreferences();

  const [emailEnabled, setEmailEnabled] = useState(true);
  const [smsEnabled, setSmsEnabled] = useState(false);
  const [voiceEnabled, setVoiceEnabled] = useState(false);
  const [pushEnabled, setPushEnabled] = useState(true);

  useEffect(() => {
    if (!prefs) return;
    setEmailEnabled(prefs.emailEnabled);
    setSmsEnabled(prefs.smsEnabled);
    setVoiceEnabled(prefs.voiceEnabled);
    setPushEnabled(prefs.pushEnabled);
  }, [prefs]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    const roleChanged = role !== user.role && user.role !== "Admin";
    const profileChanged =
      firstName.trim() !== (user.firstName ?? "") ||
      lastName.trim() !== (user.lastName ?? "") ||
      phoneNumber.trim() !== (user.phoneNumber ?? "");
    const prefsChanged =
      !!prefs &&
      (emailEnabled !== prefs.emailEnabled ||
        smsEnabled !== prefs.smsEnabled ||
        voiceEnabled !== prefs.voiceEnabled ||
        pushEnabled !== prefs.pushEnabled);

    if (roleChanged) onChangeRole(user.id, role);

    if (prefsChanged) {
      updatePrefs.mutate({
        id: user.id,
        data: {
          emailEnabled,
          smsEnabled,
          voiceEnabled,
          pushEnabled,
          quietHoursStart: prefs?.quietHoursStart,
          quietHoursEnd: prefs?.quietHoursEnd,
          timezone: prefs?.timezone,
        },
      });
    }

    if (profileChanged) {
      onUpdateUser(user.id, {
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        phoneNumber: phoneNumber.trim(),
      });
    } else {
      onClose();
    }
  };

  const channels = [
    { key: "email", label: t("users.channelEmail"), icon: Mail, value: emailEnabled, set: setEmailEnabled },
    { key: "sms", label: t("users.channelSms"), icon: MessageSquare, value: smsEnabled, set: setSmsEnabled },
    { key: "voice", label: t("users.channelVoice"), icon: PhoneCall, value: voiceEnabled, set: setVoiceEnabled },
    { key: "push", label: t("users.channelPush"), icon: Bell, value: pushEnabled, set: setPushEnabled },
  ];

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
            className="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-white/10 bg-gray-900 p-6 shadow-2xl"
          >
            <div className="mb-6">
              <h2 className="text-2xl font-bold text-white">{t("users.editUser")}</h2>
              <div className="mt-4 flex items-center gap-3">
                <div className={`flex h-12 w-12 items-center justify-center rounded-full text-lg font-bold text-white ${getAvatarColor(user.id)}`}>
                  {getUserInitials(user)}
                </div>
                <div>
                  <p className="font-medium text-white">{getUserFullName(user)}</p>
                  <p className="text-sm text-gray-400">{user.email}</p>
                </div>
              </div>
            </div>

            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.firstName")}</label>
                  <input
                    type="text"
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    placeholder={t("users.firstName")}
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                  />
                </div>
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.lastName")}</label>
                  <input
                    type="text"
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    placeholder={t("users.lastName")}
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                  />
                </div>
              </div>

              <div>
                <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.phoneNumber")}</label>
                <input
                  type="tel"
                  value={phoneNumber}
                  onChange={(e) => setPhoneNumber(e.target.value)}
                  placeholder="+905551112233"
                  className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 font-mono text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                />
                <div className="mt-2 flex items-start gap-2">
                  <Shield className="h-4 w-4 text-gray-400 mt-0.5 shrink-0" />
                  <p className="text-xs text-gray-400">{t("users.phoneCallHint")}</p>
                </div>
              </div>

              {user.role !== "Admin" && (
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.colRole")}</label>
                  <select
                    value={role}
                    onChange={(e) => setRole(e.target.value)}
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                  >
                    {roleOptions.map((roleOption) => (
                      <option key={roleOption} value={roleOption} className="bg-gray-900">
                        {roleOption}
                      </option>
                    ))}
                  </select>
                </div>
              )}

              <div>
                <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.notificationChannels")}</label>
                <div className="space-y-2">
                  {channels.map(({ key, label, icon: Icon, value, set }) => (
                    <button
                      key={key}
                      type="button"
                      onClick={() => set(!value)}
                      className="flex w-full items-center justify-between rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 transition-colors hover:bg-white/10"
                    >
                      <span className="flex items-center gap-2 text-sm text-white">
                        <Icon className="h-4 w-4 text-gray-400" />
                        {label}
                      </span>
                      <span
                        className={`relative h-5 w-9 rounded-full transition-colors ${value ? "bg-brand-500" : "bg-white/15"}`}
                      >
                        <span
                          className={`absolute top-0.5 h-4 w-4 rounded-full bg-white transition-all ${value ? "left-[18px]" : "left-0.5"}`}
                        />
                      </span>
                    </button>
                  ))}
                </div>
                <p className="mt-2 text-xs text-gray-400">{t("users.notificationChannelsHint")}</p>
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
                  disabled={isSaving}
                  className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-brand-600 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {isSaving && <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />}
                  {t("common.saveChanges")}
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}
