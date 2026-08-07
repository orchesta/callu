import { useState, useEffect } from "react";
import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { LockOpen, Eye, EyeOff, CheckCircle2, AlertCircle, XCircle } from "lucide-react";
import { authApi } from "../api/auth.api";
import { t } from "@/shared/locales/i18n";

interface PasswordRequirement {
  label: string;
  test: (password: string) => boolean;
}

const passwordRequirements: PasswordRequirement[] = [
  { label: "auth.passwordReqMinChars", test: (p) => p.length >= 12 },
  { label: "auth.passwordReqUppercase", test: (p) => /[A-Z]/.test(p) },
  { label: "auth.passwordReqLowercase", test: (p) => /[a-z]/.test(p) },
  { label: "auth.passwordReqNumber", test: (p) => /\d/.test(p) },
  { label: "auth.passwordReqSpecial", test: (p) => /[^A-Za-z0-9]/.test(p) },
  { label: "auth.passwordReqUnique", test: (p) => new Set(p).size >= 4 },
];

export function ResetPassword() {
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);
  const [error, setError] = useState("");
  const [isValidLink, setIsValidLink] = useState(true);
  const [tokenEmail, setTokenEmail] = useState("");
  const [resetToken, setResetToken] = useState("");

  useEffect(() => {
    const urlParams = new URLSearchParams(window.location.search);
    const token = urlParams.get("token");
    const email = urlParams.get("email");

    if (!token || !email) {
      setIsValidLink(false);
    } else {
      setResetToken(token);
      setTokenEmail(email);
    }
  }, []);

  const checkRequirement = (requirement: PasswordRequirement): boolean => {
    return requirement.test(password);
  };

  const passwordsMatch = password === confirmPassword && password.length > 0;
  const allRequirementsMet = passwordRequirements.every((req) => checkRequirement(req)) && passwordsMatch;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!allRequirementsMet) {
      setError(t("auth.meetAllPasswordRequirements"));
      return;
    }

    setIsLoading(true);
    setError("");

    try {
      await authApi.resetPassword(tokenEmail, resetToken, password);
      setIsSuccess(true);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("auth.resetPasswordFailed"));
    } finally {
      setIsLoading(false);
    }
  };

  if (!isValidLink) {
    return (
      <div className="min-h-screen flex items-center justify-center p-8">
        <div className="w-full max-w-md">
          <div className="bg-card/80 backdrop-blur-xl border border-border rounded-2xl p-8 text-center space-y-6">
            <div className="flex justify-center">
              <div className="w-20 h-20 rounded-full bg-error-500/10 flex items-center justify-center">
                <div className="w-16 h-16 rounded-full bg-error-500/20 flex items-center justify-center">
                  <XCircle className="w-10 h-10 text-error-500" />
                </div>
              </div>
            </div>

            <div className="space-y-2">
              <h2 style={{ fontSize: '1.5rem', fontWeight: 600 }}>
                {t("auth.invalidOrExpiredLink")}
              </h2>
              <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                {t("auth.invalidLinkMessage")}
              </p>
            </div>

            <div className="space-y-3">
              <Link to="/auth/forgot-password">
                <Button
                  className="w-full bg-gradient-to-r from-brand-500 to-brand-600 hover:from-brand-600 hover:to-brand-600 text-white"
                  style={{ fontSize: '0.875rem', fontWeight: 500 }}
                >
                  {t("auth.requestNewLink")}
                </Button>
              </Link>
              <Link to="/login">
                <Button
                  variant="outline"
                  className="w-full bg-input-background backdrop-blur-sm"
                  style={{ fontSize: '0.875rem', fontWeight: 500 }}
                >
                  {t("auth.backToLogin")}
                </Button>
              </Link>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (isSuccess) {
    return (
      <div className="min-h-screen flex items-center justify-center p-8">
        <div className="w-full max-w-md">
          <div className="bg-card/80 backdrop-blur-xl border border-border rounded-2xl p-8 text-center space-y-6">
            <div className="flex justify-center">
              <div className="w-20 h-20 rounded-full bg-success-500/10 flex items-center justify-center">
                <div className="w-16 h-16 rounded-full bg-success-500/20 flex items-center justify-center">
                  <CheckCircle2 className="w-10 h-10 text-success-500" />
                </div>
              </div>
            </div>

            <div className="space-y-2">
              <h2 style={{ fontSize: '1.5rem', fontWeight: 600 }}>
                {t("auth.passwordResetSuccessful")}
              </h2>
              <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                {t("auth.passwordResetSuccessMessage")}
              </p>
            </div>

            <Link to="/login">
              <Button
                className="w-full bg-gradient-to-r from-brand-500 to-brand-600 hover:from-brand-600 hover:to-brand-600 text-white shadow-lg shadow-brand-500/20"
                style={{ fontSize: '0.875rem', fontWeight: 500 }}
              >
                {t("auth.goToLogin")}
              </Button>
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center p-8">
      <div className="w-full max-w-md space-y-8">
        <div className="text-center">
          <Link to="/" className="inline-flex items-center gap-3 mb-6">
            <img src="/callu-logo.png" alt="Callu" className="h-9 w-auto" />
          </Link>
        </div>

        <div className="bg-card/80 backdrop-blur-xl border border-border rounded-2xl p-8 space-y-6">
          <div className="text-center space-y-2">
            <div className="flex justify-center mb-4">
              <div className="w-16 h-16 rounded-full bg-brand-500/10 flex items-center justify-center">
                <LockOpen className="w-8 h-8 text-brand-500" />
              </div>
            </div>
            <h1 style={{ fontSize: '1.5rem', fontWeight: 600 }}>
              {t("auth.resetPassword")}
            </h1>
            <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
              {t("auth.createNewSecurePassword")}
            </p>
          </div>

          {error && (
            <Alert className="border-error-500 bg-error-500/10">
              <AlertCircle className="h-4 w-4 text-error-500" />
              <AlertDescription className="text-error-500">{error}</AlertDescription>
            </Alert>
          )}

          <form onSubmit={handleSubmit} className="space-y-6">
            <div className="space-y-2">
              <Label htmlFor="password" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                {t("auth.newPassword")}
              </Label>
              <div className="relative">
                <Input
                  id="password"
                  type={showPassword ? "text" : "password"}
                  placeholder={t("auth.passwordMaskedPlaceholder")}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  className="pr-10 bg-input-background backdrop-blur-sm border-border"
                  style={{ fontSize: '0.875rem' }}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                >
                  {showPassword ? <EyeOff className="h-5 w-5" /> : <Eye className="h-5 w-5" />}
                </button>
              </div>
            </div>

            <div className="space-y-2">
              <Label htmlFor="confirmPassword" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                {t("auth.confirmPassword")}
              </Label>
              <div className="relative">
                <Input
                  id="confirmPassword"
                  type={showConfirmPassword ? "text" : "password"}
                  placeholder={t("auth.passwordMaskedPlaceholder")}
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  required
                  className="pr-10 bg-input-background backdrop-blur-sm border-border"
                  style={{ fontSize: '0.875rem' }}
                />
                <button
                  type="button"
                  onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                >
                  {showConfirmPassword ? <EyeOff className="h-5 w-5" /> : <Eye className="h-5 w-5" />}
                </button>
              </div>
            </div>

            <div className="space-y-3 p-4 rounded-lg bg-surface/30 border border-border/50">
              <p style={{ fontSize: '0.75rem', fontWeight: 600, color: '#94A3B8' }}>
                {t("auth.passwordRequirements")}
              </p>
              <div className="space-y-2">
                {passwordRequirements.map((requirement, index) => {
                  const isMet = checkRequirement(requirement);
                  return (
                    <div key={index} className="flex items-center gap-2">
                      {isMet ? (
                        <CheckCircle2 className="w-4 h-4 text-success-500 flex-shrink-0" />
                      ) : (
                        <div className="w-4 h-4 rounded-full border-2 border-muted-foreground/30 flex-shrink-0" />
                      )}
                      <span
                        style={{
                          fontSize: '0.875rem',
                          color: isMet ? '#22C55E' : '#94A3B8',
                        }}
                      >
                        {t(requirement.label)}
                      </span>
                    </div>
                  );
                })}
                <div className="flex items-center gap-2">
                  {passwordsMatch ? (
                    <CheckCircle2 className="w-4 h-4 text-success-500 flex-shrink-0" />
                  ) : (
                    <div className="w-4 h-4 rounded-full border-2 border-muted-foreground/30 flex-shrink-0" />
                  )}
                  <span
                    style={{
                      fontSize: '0.875rem',
                      color: passwordsMatch ? '#22C55E' : '#94A3B8',
                    }}
                  >
                    {t("auth.passwordsMatch")}
                  </span>
                </div>
              </div>
            </div>

            <Button
              type="submit"
              className="w-full bg-gradient-to-r from-brand-500 to-brand-600 hover:from-brand-600 hover:to-brand-600 text-white shadow-lg shadow-brand-500/20"
              disabled={isLoading || !allRequirementsMet}
              style={{ fontSize: '0.875rem', fontWeight: 500 }}
            >
              {isLoading ? (
                <div className="flex items-center gap-2">
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                  {t("auth.resettingPassword")}
                </div>
              ) : (
                t("auth.resetPassword")
              )}
            </Button>
          </form>
        </div>
      </div>
    </div>
  );
}
