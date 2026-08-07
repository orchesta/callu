import { useState } from "react";
import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { Key, ArrowLeft, AlertCircle, CheckCircle2, Send } from "lucide-react";
import { authApi } from "../api/auth.api";
import { t } from "@/shared/locales/i18n";

export function ForgotPassword() {
  const [email, setEmail] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [isSubmitted, setIsSubmitted] = useState(false);
  const [error, setError] = useState("");

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setError("");

    try {
      await authApi.forgotPassword(email);
      setIsSubmitted(true);
    } catch {
      setIsSubmitted(true);
    } finally {
      setIsLoading(false);
    }
  };

  if (isSubmitted) {
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
                {t("auth.checkYourEmail")}
              </h2>
              <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                {t("auth.resetLinkSentTo")}
              </p>
              <p style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                {email}
              </p>
              <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '1rem' }}>
                {t("auth.checkSpamFolder")}
              </p>
            </div>

            <Link to="/login">
              <Button
                variant="outline"
                className="w-full bg-input-background backdrop-blur-sm"
                style={{ fontSize: '0.875rem', fontWeight: 500 }}
              >
                <ArrowLeft className="w-4 h-4 mr-2" />
                {t("auth.backToLogin")}
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
                <Key className="w-8 h-8 text-brand-500" />
              </div>
            </div>
            <h1 style={{ fontSize: '1.5rem', fontWeight: 600 }}>
              {t("auth.forgotPassword")}
            </h1>
            <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
              {t("auth.forgotPasswordDesc")}
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
              <Label htmlFor="email" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                {t("auth.emailAddress")}
              </Label>
              <Input
                id="email"
                type="email"
                placeholder={t("auth.emailPlaceholder")}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                className="bg-input-background backdrop-blur-sm border-border"
                style={{ fontSize: '0.875rem' }}
              />
            </div>

            <Button
              type="submit"
              className="w-full bg-gradient-to-r from-brand-500 to-brand-600 hover:from-brand-600 hover:to-brand-600 text-white shadow-lg shadow-brand-500/20"
              disabled={isLoading}
              style={{ fontSize: '0.875rem', fontWeight: 500 }}
            >
              {isLoading ? (
                <div className="flex items-center gap-2">
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                  {t("auth.sending")}
                </div>
              ) : (
                <>
                  <Send className="w-4 h-4 mr-2" />
                  {t("auth.sendResetLink")}
                </>
              )}
            </Button>
          </form>

          <div className="text-center">
            <Link
              to="/login"
              className="inline-flex items-center gap-2 hover:text-brand-400 transition-colors"
              style={{ fontSize: '0.875rem', color: '#3E7BFA' }}
            >
              <ArrowLeft className="w-4 h-4" />
              {t("auth.backToLogin")}
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
