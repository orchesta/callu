import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { ShieldBan, Home, ArrowLeft } from "lucide-react";
import { t } from "@/shared/locales/i18n";

export function AccessDenied() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4">
      <div className="text-center space-y-6 max-w-md">
        <div className="flex justify-center">
          <div className="w-24 h-24 rounded-full bg-error-500/10 flex items-center justify-center">
            <ShieldBan className="w-12 h-12 text-error-500" />
          </div>
        </div>

        <div className="space-y-2">
          <h1 style={{ fontSize: "1.875rem", fontWeight: 700 }}>
            {t("auth.accessDenied")}
          </h1>
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {t("auth.accessDeniedMessage")}
          </p>
        </div>

        <div className="flex flex-col sm:flex-row items-center justify-center gap-3">
          <Button asChild className="bg-brand-500 hover:bg-brand-600 text-white">
            <Link to="/dashboard">
              <Home className="w-4 h-4 mr-2" />
              {t("auth.goToDashboard")}
            </Link>
          </Button>
          <Button
            variant="outline"
            className="bg-input-background"
            onClick={() => window.history.back()}
          >
            <ArrowLeft className="w-4 h-4 mr-2" />
            {t("common.goBack")}
          </Button>
        </div>
      </div>
    </div>
  );
}

export default AccessDenied;
