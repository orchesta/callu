import { useState, useMemo } from "react";
import { Link, useSearchParams } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import {
  Eye,
  EyeOff,
  AlertCircle,
  CheckCircle,
  Shield,
  Loader2,
  UserPlus,
  ArrowLeft,
} from "lucide-react";
import { authApi } from "../api/auth.api";
import { t } from "@/shared/locales/i18n";

enum PageState {
  Loading = "loading",
  Form = "form",
  Success = "success",
  Error = "error",
}

const passwordRules = [
  { id: "length", label: "auth.passwordReqMinChars", test: (p: string) => p.length >= 12 },
  { id: "upper", label: "auth.passwordReqUppercase", test: (p: string) => /[A-Z]/.test(p) },
  { id: "lower", label: "auth.passwordReqLowercase", test: (p: string) => /[a-z]/.test(p) },
  { id: "digit", label: "auth.passwordReqNumber", test: (p: string) => /[0-9]/.test(p) },
  { id: "special", label: "auth.passwordReqSpecial", test: (p: string) => /[^A-Za-z0-9]/.test(p) },
  { id: "unique", label: "auth.passwordReqUnique", test: (p: string) => new Set(p).size >= 4 },
  { id: "match", label: "auth.passwordsMatch", test: (_p: string, c: string, p: string) => p.length > 0 && p === c },
];

export function AcceptInvitation() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token");
  const email = searchParams.get("email") || "";

  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [pageState, setPageState] = useState<PageState>(token ? PageState.Form : PageState.Error);
  const [errorMessage, setErrorMessage] = useState(
    !token ? t("auth.invitationLinkInvalid") : ""
  );

  const passedRules = useMemo(() => {
    return passwordRules.map((rule) => ({
      ...rule,
      passed:
        rule.id === "match"
          ? rule.test(password, confirmPassword, password)
          : rule.test(password, "", ""),
    }));
  }, [password, confirmPassword]);

  const allRulesPassed = passedRules.every((r) => r.passed);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!allRulesPassed) return;

    setIsLoading(true);
    try {
      if (!email) throw new Error(t("auth.missingEmailParam"));
      await authApi.acceptInvitation(email, token!, password);
      setPageState(PageState.Success);
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : t("auth.acceptInvitationFailed"));
      setPageState(PageState.Error);
    } finally {
      setIsLoading(false);
    }
  };

  if (pageState === PageState.Loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background">
        <div className="flex flex-col items-center gap-4">
          <Loader2 className="w-12 h-12 animate-spin text-brand-500" />
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {t("auth.loadingInvitationDetails")}
          </p>
        </div>
      </div>
    );
  }

  if (pageState === PageState.Error) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background px-4">
        <div className="w-full max-w-md text-center space-y-6">
          <div className="flex justify-center">
            <div className="w-20 h-20 rounded-full bg-error-500/10 flex items-center justify-center">
              <AlertCircle className="w-10 h-10 text-error-500" />
            </div>
          </div>
          <div>
            <h1 style={{ fontSize: "1.5rem", fontWeight: 700, marginBottom: "0.5rem" }}>
              {t("auth.invalidInvitation")}
            </h1>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{errorMessage}</p>
          </div>
          <Link to="/login">
            <Button variant="outline" className="bg-input-background">
              <ArrowLeft className="w-4 h-4 mr-2" />
              {t("auth.backToLogin")}
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  if (pageState === PageState.Success) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background px-4">
        <div className="w-full max-w-md text-center space-y-6">
          <div className="flex justify-center">
            <div className="w-20 h-20 rounded-full bg-success-500/10 flex items-center justify-center">
              <CheckCircle className="w-10 h-10 text-success-500" />
            </div>
          </div>
          <div>
            <h1 style={{ fontSize: "1.5rem", fontWeight: 700, marginBottom: "0.5rem" }}>
              {t("auth.accountActivated")}
            </h1>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("auth.accountActivatedMessage")}
            </p>
          </div>
          <Link to="/login">
            <Button className="bg-brand-500 hover:bg-brand-600 text-white w-full">
              {t("auth.goToLogin")}
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4">
      <div className="w-full max-w-md space-y-8">
        <div className="text-center space-y-4">
          <div className="flex justify-center">
            <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-brand-500 to-brand-600 flex items-center justify-center">
              <UserPlus className="w-8 h-8 text-white" />
            </div>
          </div>
          <div>
            <h1 style={{ fontSize: "1.5rem", fontWeight: 700 }}>{t("auth.welcomeToCalluApp")}</h1>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.5rem" }}>
              {t("auth.setUpYourAccount")}
            </p>
          </div>
        </div>

        <div className="bg-brand-500/10 border border-brand-500/20 rounded-lg px-4 py-3 text-center">
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {t("auth.settingUpAccountFor")}{" "}
            <span style={{ fontWeight: 600, color: "#3E7BFA" }}>{email}</span>
          </p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-6">
          <div className="space-y-2">
            <Label htmlFor="password">{t("auth.createPassword")}</Label>
            <div className="relative">
              <Input
                id="password"
                type={showPassword ? "text" : "password"}
                placeholder={t("auth.enterYourPassword")}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="bg-input-background pr-10"
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
              >
                {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="confirmPassword">{t("auth.confirmPassword")}</Label>
            <div className="relative">
              <Input
                id="confirmPassword"
                type={showPassword ? "text" : "password"}
                placeholder={t("auth.confirmYourPassword")}
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                className="bg-input-background pr-10"
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2">
            {passedRules.map((rule) => (
              <div key={rule.id} className="flex items-center gap-2">
                <CheckCircle
                  className={`w-4 h-4 flex-shrink-0 ${rule.passed ? "text-success-500" : "text-muted-foreground/40"
                    }`}
                />
                <span
                  style={{
                    fontSize: "0.75rem",
                    color: rule.passed ? "#22C55E" : "#94A3B8",
                    fontWeight: rule.passed ? 500 : 400,
                  }}
                >
                  {t(rule.label)}
                </span>
              </div>
            ))}
          </div>

          <Button
            type="submit"
            className="w-full bg-brand-500 hover:bg-brand-600 text-white"
            disabled={!allRulesPassed || isLoading}
          >
            {isLoading ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t("auth.settingUp")}
              </>
            ) : (
              <>
                <Shield className="w-4 h-4 mr-2" />
                {t("auth.activateAccount")}
              </>
            )}
          </Button>
        </form>

        <p className="text-center" style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
          {t("auth.alreadyHaveAccount")}{" "}
          <Link to="/login" className="text-brand-500 hover:text-brand-400 transition-colors" style={{ fontWeight: 500 }}>
            {t("auth.signIn")}
          </Link>
        </p>
      </div>
    </div>
  );
}

export default AcceptInvitation;
