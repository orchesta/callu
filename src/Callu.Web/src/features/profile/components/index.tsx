import { useState, useEffect } from "react";
import { t } from "@/shared/locales/i18n";
import { User, Shield, Bell, Save, Eye, EyeOff, Check, X, Loader2, AlertCircle } from "lucide-react";
import { motion } from "motion/react";
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { useProfile, useUpdateProfile, useChangePassword, useNotificationPreferences, useUpdateNotificationPreferences } from "../hooks/use-profile";
import { useTimezones } from "@/features/settings/hooks/use-settings";

/**
 * User Profile Page
 *
 * Allows users to manage their:
 * - Personal information (name, email, phone, timezone)
 * - Security settings (password change)
 * - Notification preferences — Coming Soon (no BE endpoint yet)
 */

interface PasswordValidation {
  hasMinLength: boolean;
  hasUppercase: boolean;
  hasLowercase: boolean;
  hasNumber: boolean;
  hasSpecialChar: boolean;
  hasUniqueChars: boolean;
}

export function ProfilePage() {
  const { data: profile, isLoading, error } = useProfile();
  const updateProfileMutation = useUpdateProfile();
  const changePasswordMutation = useChangePassword();
  const { data: timezones, isLoading: isLoadingTimezones } = useTimezones();

  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [phone, setPhone] = useState("");
  const [timezone, setTimezone] = useState("America/New_York");

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showCurrentPassword, setShowCurrentPassword] = useState(false);
  const [showNewPassword, setShowNewPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);

  const { data: notifPrefs, isLoading: notifPrefsLoading } = useNotificationPreferences();
  const updateNotifPrefsMutation = useUpdateNotificationPreferences();
  const [emailOn, setEmailOn] = useState(true);
  const [smsOn, setSmsOn] = useState(false);
  const [voiceOn, setVoiceOn] = useState(true);
  const [pushOn, setPushOn] = useState(true);
  const [quietStart, setQuietStart] = useState("");
  const [quietEnd, setQuietEnd] = useState("");

  useEffect(() => {
    if (profile) {
      setFirstName(profile.firstName || "");
      setLastName(profile.lastName || "");
      setPhone(profile.phoneNumber || "");
      setTimezone(profile.timezone || "America/New_York");
    }
  }, [profile]);

  useEffect(() => {
    if (notifPrefs) {
      setEmailOn(notifPrefs.emailEnabled);
      setSmsOn(notifPrefs.smsEnabled);
      setVoiceOn(notifPrefs.voiceEnabled);
      setPushOn(notifPrefs.pushEnabled);
      setQuietStart(notifPrefs.quietHoursStart ?? "");
      setQuietEnd(notifPrefs.quietHoursEnd ?? "");
    }
  }, [notifPrefs]);

  const validatePassword = (password: string): PasswordValidation => {
    return {
      hasMinLength: password.length >= 12,
      hasUppercase: /[A-Z]/.test(password),
      hasLowercase: /[a-z]/.test(password),
      hasNumber: /[0-9]/.test(password),
      hasSpecialChar: /[^A-Za-z0-9]/.test(password),
      hasUniqueChars: new Set(password).size >= 4,
    };
  };

  const passwordValidation = validatePassword(newPassword);
  const isPasswordValid =
    Object.values(passwordValidation).every((v) => v) &&
    newPassword === confirmPassword &&
    currentPassword.length > 0;

  const handleSavePersonalInfo = () => {
    updateProfileMutation.mutate({
      firstName,
      lastName,
      phoneNumber: phone || undefined,
      timezone,
    });
  };

  const handleSaveNotifPrefs = () => {
    updateNotifPrefsMutation.mutate({
      emailEnabled: emailOn,
      smsEnabled: smsOn,
      voiceEnabled: voiceOn,
      pushEnabled: pushOn,
      quietHoursStart: quietStart || null,
      quietHoursEnd: quietEnd || null,
      timezone: notifPrefs?.timezone ?? null,
    });
  };

  const handleSavePassword = () => {
    if (!isPasswordValid) return;
    changePasswordMutation.mutate(
      { currentPassword, newPassword },
      {
        onSuccess: () => {
          setCurrentPassword("");
          setNewPassword("");
          setConfirmPassword("");
        },
      },
    );
  };

  const initials = profile
    ? `${profile.firstName?.charAt(0) || ""}${profile.lastName?.charAt(0) || ""}`.toUpperCase() || "?"
    : "?";

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <Loader2 className="w-8 h-8 animate-spin text-brand-500 mx-auto mb-3" />
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("profile.loading")}</p>
        </div>
      </div>
    );
  }

  if (error || !profile) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <AlertCircle className="w-8 h-8 text-error-500 mx-auto mb-3" />
          <p style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "0.5rem" }}>{t("profile.loadFailed")}</p>
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {error instanceof Error ? error.message : t("common.errorOccurred")}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6">
      <div className="mx-auto max-w-5xl space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white">{t("profile.title")}</h1>
          <p className="mt-2 text-gray-400">
            {t("profile.subtitle")}
          </p>
        </div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="overflow-hidden rounded-xl border border-white/10 bg-white/5 backdrop-blur-xl"
        >
          <div className="border-b border-white/10 bg-gradient-to-r from-brand-500/10 to-transparent p-6">
            <div className="flex items-center gap-6">
              <div className="relative flex h-24 w-24 items-center justify-center rounded-full text-3xl font-bold text-white bg-brand-500">
                {initials}
              </div>
              <div>
                <h2 className="text-2xl font-bold text-white">{profile.firstName} {profile.lastName}</h2>
                <p className="mt-1 text-gray-400">{profile.email}</p>
                <div className="mt-2 flex items-center gap-3">
                  <span className="text-sm text-gray-500">
                    {t("profile.memberSince")}{" "}
                    {new Date(profile.createdAt).toLocaleDateString("en-US", { month: "long", year: "numeric" })}
                  </span>
                </div>
              </div>
            </div>
          </div>
        </motion.div>

        <div className="grid gap-6 lg:grid-cols-3">
          <div className="space-y-6 lg:col-span-2">
            <motion.div
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: 0.1 }}
              className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl"
            >
              <div className="mb-6 flex items-center gap-3">
                <div className="rounded-lg bg-brand-500/20 p-2">
                  <User className="h-5 w-5 text-brand-400" />
                </div>
                <h3 className="text-xl font-semibold text-white">{t("profile.personalInfo")}</h3>
              </div>

              <div className="space-y-4">
                <div className="grid gap-4 sm:grid-cols-2">
                  <div>
                    <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.firstName")}</label>
                    <input
                      type="text"
                      value={firstName}
                      onChange={(e) => setFirstName(e.target.value)}
                      className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                    />
                  </div>
                  <div>
                    <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.lastName")}</label>
                    <input
                      type="text"
                      value={lastName}
                      onChange={(e) => setLastName(e.target.value)}
                      className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                    />
                  </div>
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.emailAddress")}</label>
                  <input
                    type="email"
                    value={profile.email}
                    disabled
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-gray-500 backdrop-blur-xl"
                  />
                  <p className="mt-1 text-xs text-gray-500">{t("profile.emailCannotChange")}</p>
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.phoneNumber")}</label>
                  <PhoneInput
                    value={phone}
                    onChange={(val) => setPhone(val || "")}
                    className="w-full"
                  />
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.timezoneLabel")}</label>
                  <select
                    value={timezone}
                    onChange={(e) => setTimezone(e.target.value)}
                    disabled={isLoadingTimezones}
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 disabled:opacity-50"
                  >
                    {isLoadingTimezones ? (
                      <option value="">{t("profile.loadingTimezones")}</option>
                    ) : (
                      (timezones ?? []).map((tz) => (
                        <option key={tz.id} value={tz.id} className="bg-gray-900">
                          {tz.displayName}
                        </option>
                      ))
                    )}
                  </select>
                </div>

                <button
                  onClick={handleSavePersonalInfo}
                  disabled={updateProfileMutation.isPending}
                  className="flex items-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-brand-600 disabled:opacity-50"
                >
                  {updateProfileMutation.isPending ? (
                    <>
                      <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                      {t("common.saving")}
                    </>
                  ) : (
                    <>
                      <Save className="h-4 w-4" />
                      {t("common.saveChanges")}
                    </>
                  )}
                </button>
              </div>
            </motion.div>

            <motion.div
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: 0.2 }}
              className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl"
            >
              <div className="mb-6 flex items-center gap-3">
                <div className="rounded-lg bg-purple-500/20 p-2">
                  <Shield className="h-5 w-5 text-purple-400" />
                </div>
                <h3 className="text-xl font-semibold text-white">{t("profile.security")}</h3>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.currentPassword")}</label>
                  <div className="relative">
                    <input
                      type={showCurrentPassword ? "text" : "password"}
                      value={currentPassword}
                      onChange={(e) => setCurrentPassword(e.target.value)}
                      placeholder={t("auth.passwordMaskedPlaceholder")}
                      className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 pr-10 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                    />
                    <button
                      type="button"
                      onClick={() => setShowCurrentPassword(!showCurrentPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-300"
                    >
                      {showCurrentPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.newPassword")}</label>
                  <div className="relative">
                    <input
                      type={showNewPassword ? "text" : "password"}
                      value={newPassword}
                      onChange={(e) => setNewPassword(e.target.value)}
                      placeholder={t("auth.passwordMaskedPlaceholder")}
                      className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 pr-10 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                    />
                    <button
                      type="button"
                      onClick={() => setShowNewPassword(!showNewPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-300"
                    >
                      {showNewPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("profile.confirmNewPassword")}</label>
                  <div className="relative">
                    <input
                      type={showConfirmPassword ? "text" : "password"}
                      value={confirmPassword}
                      onChange={(e) => setConfirmPassword(e.target.value)}
                      placeholder={t("auth.passwordMaskedPlaceholder")}
                      className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 pr-10 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                    />
                    <button
                      type="button"
                      onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-300"
                    >
                      {showConfirmPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </div>

                {newPassword && (
                  <div className="rounded-lg border border-white/10 bg-white/5 p-4">
                    <p className="mb-3 text-sm font-medium text-gray-300">{t("profile.passwordRequirements")}</p>
                    <div className="grid gap-2 sm:grid-cols-2">
                      <ValidationRule label={t("profile.minChars")} isValid={passwordValidation.hasMinLength} />
                      <ValidationRule label={t("profile.oneUppercase")} isValid={passwordValidation.hasUppercase} />
                      <ValidationRule label={t("profile.oneLowercase")} isValid={passwordValidation.hasLowercase} />
                      <ValidationRule label={t("profile.oneNumber")} isValid={passwordValidation.hasNumber} />
                      <ValidationRule label={t("profile.oneSpecialChar")} isValid={passwordValidation.hasSpecialChar} />
                      <ValidationRule label={t("profile.uniqueChars")} isValid={passwordValidation.hasUniqueChars} />
                      {confirmPassword && (
                        <ValidationRule label={t("profile.passwordsMatch")} isValid={newPassword === confirmPassword} />
                      )}
                    </div>
                  </div>
                )}

                <button
                  onClick={handleSavePassword}
                  disabled={!isPasswordValid || changePasswordMutation.isPending}
                  className="flex items-center gap-2 rounded-lg bg-purple-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-purple-600 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {changePasswordMutation.isPending ? (
                    <>
                      <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                      {t("profile.updating")}
                    </>
                  ) : (
                    <>
                      <Shield className="h-4 w-4" />
                      {t("profile.updatePassword")}
                    </>
                  )}
                </button>
              </div>
            </motion.div>
          </div>

          <div className="space-y-6">
            <motion.div
              initial={{ opacity: 0, x: 20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: 0.3 }}
              className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl"
            >
              <div className="mb-6 flex items-center gap-3">
                <div className="rounded-lg bg-amber-500/20 p-2">
                  <Bell className="h-5 w-5 text-amber-400" />
                </div>
                <h3 className="text-xl font-semibold text-white">{t("profile.notificationsTitle")}</h3>
              </div>

              {notifPrefsLoading ? (
                <div className="flex items-center justify-center py-8">
                  <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
                </div>
              ) : (
                <div className="space-y-5">
                  <div className="flex items-start justify-between">
                    <div className="flex-1">
                      <h4 className="font-medium text-white">{t("profile.emailNotificationsLabel")}</h4>
                      <p className="mt-1 text-sm text-gray-400">{t("profile.emailNotificationsDesc")}</p>
                    </div>
                    <ToggleSwitch checked={emailOn} onChange={setEmailOn} />
                  </div>

                  <div className="flex items-start justify-between">
                    <div className="flex-1">
                      <h4 className="font-medium text-white">{t("profile.smsNotificationsLabel")}</h4>
                      <p className="mt-1 text-sm text-gray-400">{t("profile.smsNotificationsDesc")}</p>
                    </div>
                    <ToggleSwitch checked={smsOn} onChange={setSmsOn} />
                  </div>

                  <div className="flex items-start justify-between">
                    <div className="flex-1">
                      <h4 className="font-medium text-white">{t("profile.voiceCallsLabel")}</h4>
                      <p className="mt-1 text-sm text-gray-400">{t("profile.voiceCallsDesc")}</p>
                    </div>
                    <ToggleSwitch checked={voiceOn} onChange={setVoiceOn} />
                  </div>

                  <div className="flex items-start justify-between">
                    <div className="flex-1">
                      <h4 className="font-medium text-white">{t("profile.pushNotifications")}</h4>
                      <p className="mt-1 text-sm text-gray-400">{t("profile.pushNotifDesc")}</p>
                    </div>
                    <ToggleSwitch checked={pushOn} onChange={setPushOn} />
                  </div>

                  <div className="border-t border-white/10 pt-5">
                    <h4 className="font-medium text-white">{t("profile.quietHoursLabel")}</h4>
                    <p className="mt-1 mb-3 text-sm text-gray-400">{t("profile.quietHoursDesc")}</p>
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className="mb-1.5 block text-xs font-medium text-gray-400">{t("profile.quietHoursStart")}</label>
                        <input
                          type="time"
                          value={quietStart}
                          onChange={(e) => setQuietStart(e.target.value)}
                          className="w-full rounded-lg border border-white/10 bg-white/5 px-3 py-2 text-white backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                        />
                      </div>
                      <div>
                        <label className="mb-1.5 block text-xs font-medium text-gray-400">{t("profile.quietHoursEnd")}</label>
                        <input
                          type="time"
                          value={quietEnd}
                          onChange={(e) => setQuietEnd(e.target.value)}
                          className="w-full rounded-lg border border-white/10 bg-white/5 px-3 py-2 text-white backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                        />
                      </div>
                    </div>
                  </div>

                  <button
                    onClick={handleSaveNotifPrefs}
                    disabled={updateNotifPrefsMutation.isPending}
                    className="flex w-full items-center justify-center gap-2 rounded-lg bg-amber-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-amber-600 disabled:opacity-50"
                  >
                    {updateNotifPrefsMutation.isPending ? (
                      <>
                        <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                        {t("common.saving")}
                      </>
                    ) : (
                      <>
                        <Save className="h-4 w-4" />
                        {t("profile.savePreferences")}
                      </>
                    )}
                  </button>
                </div>
              )}
            </motion.div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ValidationRule({ label, isValid }: { label: string; isValid: boolean }) {
  return (
    <div className="flex items-center gap-2">
      {isValid ? (
        <Check className="h-4 w-4 text-green-500" />
      ) : (
        <X className="h-4 w-4 text-gray-500" />
      )}
      <span className={`text-sm ${isValid ? "text-green-400" : "text-gray-500"}`}>{label}</span>
    </div>
  );
}

function ToggleSwitch({ checked, onChange }: { checked: boolean; onChange: (checked: boolean) => void }) {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${checked ? "bg-brand-500" : "bg-gray-700"
        }`}
    >
      <span
        className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${checked ? "translate-x-6" : "translate-x-1"
          }`}
      />
    </button>
  );
}