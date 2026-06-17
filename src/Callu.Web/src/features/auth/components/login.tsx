import { useState, useEffect } from "react";
import { Link, useNavigate } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { Mail, Lock, Eye, EyeOff, AlertCircle, CheckCircle, CalendarClock, Route, Phone, Users } from "lucide-react";
import { useAuth } from "@/shared/auth/auth.context";
import { API_URL } from "@/shared/config";
import { t } from "@/shared/locales/i18n";

export function Login() {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [checkingSetup, setCheckingSetup] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/setup/status`);
        if (res.ok) {
          const json = await res.json();
          const setupRequired = json.data?.setupRequired ?? json.setupRequired;
          if (setupRequired) {
            navigate('/auth/initial-setup');
            return;
          }
        }
      } catch {
        /* empty */
      } finally {
        setCheckingSetup(false);
      }
    })();
  }, [navigate]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setError("");
    setSuccess("");

    try {
      await login(email, password);
      setSuccess(t("auth.loginSuccessRedirecting"));
      setTimeout(() => navigate("/dashboard"), 500);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("auth.invalidCredentials"));
    } finally {
      setIsLoading(false);
    }
  };



  if (checkingSetup) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background">
        <div className="w-6 h-6 border-2 border-brand-500/30 border-t-brand-500 rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="min-h-screen flex">
      <div className="flex-1 flex items-center justify-center p-8 lg:p-12">
        <div className="w-full max-w-md space-y-8">
          <div className="text-center space-y-2">
            <Link to="/" className="inline-flex items-center gap-3 mb-6">
              <img src="/callu-logo.png" alt="Callu" className="h-9 w-auto" />
            </Link>
            <h1 style={{ fontSize: '1.875rem', fontWeight: 600, lineHeight: 1.3 }}>
              {t("auth.signInToAccount")}
            </h1>
            <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
              {t("auth.monitorAndRespond")}
            </p>
          </div>

          {error && (
            <Alert className="border-error-500 bg-error-500/10">
              <AlertCircle className="h-4 w-4 text-error-500" />
              <AlertDescription className="text-error-500">{error}</AlertDescription>
            </Alert>
          )}

          {success && (
            <Alert className="border-success-500 bg-success-500/10">
              <CheckCircle className="h-4 w-4 text-success-500" />
              <AlertDescription className="text-success-500">{success}</AlertDescription>
            </Alert>
          )}

          <form onSubmit={handleSubmit} className="space-y-6">
            <div className="space-y-2">
              <Label htmlFor="email" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                {t("auth.emailAddress")}
              </Label>
              <div className="relative">
                <Mail className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-muted-foreground z-10 pointer-events-none" />
                <Input
                  id="email"
                  type="email"
                  placeholder={t("auth.emailPlaceholder")}
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                  className="pl-10 bg-input-background backdrop-blur-sm border-border"
                  style={{ fontSize: '0.875rem' }}
                />
              </div>
            </div>

            <div className="space-y-2">
              <Label htmlFor="password" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                {t("auth.password")}
              </Label>
              <div className="relative">
                <Lock className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-muted-foreground z-10 pointer-events-none" />
                <Input
                  id="password"
                  type={showPassword ? "text" : "password"}
                  placeholder={t("auth.passwordMaskedPlaceholder")}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  className="pl-10 pr-10 bg-input-background backdrop-blur-sm border-border"
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

            <div className="flex items-center justify-end">
              <Link
                to="/auth/forgot-password"
                className="hover:text-brand-400 transition-colors"
                style={{ fontSize: '0.875rem', color: '#3E7BFA' }}
              >
                {t("auth.forgotPasswordLink")}
              </Link>
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
                  {t("auth.signingIn")}
                </div>
              ) : (
                t("auth.signIn")
              )}
            </Button>
          </form>
        </div>
      </div>

      <div className="hidden lg:flex lg:flex-1 bg-gradient-to-br from-brand-500 to-brand-600 relative overflow-hidden">
        <div className="absolute inset-0 opacity-10">
          <div className="absolute top-0 left-0 w-96 h-96 bg-white rounded-full blur-3xl animate-pulse" />
          <div className="absolute bottom-0 right-0 w-96 h-96 bg-white rounded-full blur-3xl animate-pulse" style={{ animationDelay: '1s' }} />
        </div>

        <div className="relative z-10 flex flex-col items-center justify-center p-12 text-white text-center max-w-2xl mx-auto">
          <div className="w-24 h-24 rounded-2xl bg-white/15 backdrop-blur-md flex items-center justify-center mb-6 shadow-2xl ring-1 ring-white/20">
            <img src="/callu-icon.png" alt="" className="h-14 w-14 object-contain" />
          </div>
          <h1
            className="font-['Outfit'] tracking-tight mb-3"
            style={{ fontSize: '2.5rem', fontWeight: 700, lineHeight: 1 }}
          >
            Callu
          </h1>
          <h2 className="font-['Outfit']" style={{ fontSize: '1.5rem', fontWeight: 500, lineHeight: 1.25, marginBottom: '1rem', opacity: 0.95 }}>
            {t("auth.heroTagline")}
          </h2>
          <p style={{ fontSize: '1rem', lineHeight: 1.6, maxWidth: '460px', opacity: 0.85 }}>
            {t("auth.heroDescription")}
          </p>

          <div className="grid grid-cols-2 gap-3 mt-10 w-full max-w-md">
            {[
              { icon: CalendarClock, label: t("auth.featureOnCall") },
              { icon: Route, label: t("auth.featureSmartRouting") },
              { icon: Phone, label: t("auth.featureVoiceCalls") },
              { icon: Users, label: t("auth.featureGlobalTeams") },
            ].map(({ icon: Icon, label }) => (
              <div
                key={label}
                className="flex items-center gap-2 px-4 py-2.5 rounded-xl bg-white/10 backdrop-blur-md border border-white/20"
                style={{ fontSize: '0.875rem', fontWeight: 500 }}
              >
                <Icon className="w-4 h-4 flex-shrink-0" />
                <span className="truncate">{label.trim()}</span>
              </div>
            ))}
          </div>

          <p className="mt-10 text-xs opacity-60">
            {t("auth.heroOpenSource")}
          </p>
        </div>
      </div>
    </div>
  );
}