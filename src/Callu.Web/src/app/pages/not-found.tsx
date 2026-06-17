import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Home, ArrowLeft } from "lucide-react";
import { t } from "@/shared/locales/i18n";

export function NotFound() {
  return (
    <div className="flex items-center justify-center min-h-[calc(100vh-4rem)] p-6">
      <div className="text-center space-y-8 max-w-lg">
        <div className="relative">
          <h1
            className="font-['Outfit'] select-none"
            style={{
              fontSize: "8rem",
              fontWeight: 800,
              lineHeight: 1,
              background: "linear-gradient(135deg, #3E7BFA 0%, #3E7BFA40 100%)",
              WebkitBackgroundClip: "text",
              WebkitTextFillColor: "transparent",
              opacity: 0.6,
            }}
          >
            404
          </h1>
        </div>

        <div className="space-y-3">
          <h2 style={{ fontSize: "1.5rem", fontWeight: 600 }}>{t("shared.notFound.title")}</h2>
          <p
            style={{ fontSize: "0.875rem", color: "#94A3B8", maxWidth: "24rem", margin: "0 auto" }}
          >
            {t("shared.notFound.description")}
          </p>
        </div>

        <div className="flex flex-col sm:flex-row items-center justify-center gap-3">
          <Link to="/dashboard">
            <Button className="bg-brand-500 hover:bg-brand-600 text-white">
              <Home className="w-4 h-4 mr-2" />
              {t("shared.notFound.goDashboard")}
            </Button>
          </Link>
          <Button
            variant="outline"
            className="bg-input-background"
            type="button"
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

export default NotFound;
