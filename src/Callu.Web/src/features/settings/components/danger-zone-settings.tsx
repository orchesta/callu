import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Card } from "@/shared/components/ui/card";
import { AlertTriangle, Trash2, Download } from "lucide-react";

export function DangerZoneSettings() {
  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border border-error-500/20">
      <div className="flex items-center gap-3 mb-4">
        <AlertTriangle className="w-5 h-5 text-error-500" />
        <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>
          {t("settings.danger.title")}
        </h3>
      </div>
      <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginBottom: "1.5rem" }}>
        {t("settings.danger.subtitle")}
      </p>

      <div className="space-y-4">
        <div className="flex items-center justify-between p-4 rounded-lg border border-border">
          <div>
            <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
              {t("settings.danger.deleteIncidents")}
            </p>
            <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
              {t("settings.danger.deleteIncidentsDesc")}
            </p>
          </div>
          <Button
            variant="outline"
            className="bg-input-background text-error-500 border-error-500/20 hover:bg-error-500/10"
            disabled
            title={t("common.comingSoon")}
          >
            <Trash2 className="w-4 h-4 mr-2" />
            {t("settings.danger.deleteAll")}
          </Button>
        </div>

        <div className="flex items-center justify-between p-4 rounded-lg border border-border">
          <div>
            <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
              {t("settings.danger.exportData")}
            </p>
            <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
              {t("settings.danger.exportDataDesc")}
            </p>
          </div>
          <Button variant="outline" className="bg-input-background" disabled title={t("common.comingSoon")}>
            <Download className="w-4 h-4 mr-2" />
            {t("settings.danger.export")}
          </Button>
        </div>
      </div>
    </Card>
  );
}
