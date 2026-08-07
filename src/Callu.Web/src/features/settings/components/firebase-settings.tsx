import { useState, useEffect } from "react";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Card } from "@/shared/components/ui/card";
import { Save, Zap, Loader2 } from "lucide-react";
import {
  useFirebaseSettings,
  useSaveFirebase,
  useTestFirebase,
} from "../hooks/use-settings";
import { dateLocale } from "@/shared/utils/datetime";

export function FirebaseSettings() {
  const { data: firebaseSettings } = useFirebaseSettings();
  const saveFirebase = useSaveFirebase();
  const testFirebase = useTestFirebase();

  const [projectId, setProjectId] = useState("");
  const [serviceAccountJson, setServiceAccountJson] = useState("");
  const [clearCredential, setClearCredential] = useState(false);

  useEffect(() => {
    if (firebaseSettings) {
      setProjectId(firebaseSettings.projectId ?? "");
    }
  }, [firebaseSettings]);

  const handleSave = () => {
    saveFirebase.mutate({
      projectId: projectId.trim() || undefined,
      serviceAccountJson: clearCredential
        ? undefined
        : serviceAccountJson.trim() || undefined,
      clearCredential: clearCredential || undefined,
    }, {
      onSuccess: () => {
        setServiceAccountJson("");
        setClearCredential(false);
      },
    });
  };

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
          {t("settings.firebase.title")}
        </h3>

        <p style={{ fontSize: "0.8125rem", marginBottom: "1rem" }} className="text-muted-foreground">
          {t("settings.firebase.hint")}
        </p>

        {firebaseSettings?.isConfigured && (
          <div className="p-3 rounded-lg bg-success-500/10 border border-success-500/20 mb-4">
            <p style={{ fontSize: "0.8125rem", color: "#22C55E" }}>
              {t("settings.firebase.configured")}
              {firebaseSettings.lastTestedAt && (
                <> &middot; {t("settings.firebase.lastTested", { date: new Date(firebaseSettings.lastTestedAt).toLocaleDateString(dateLocale()) })}</>
              )}
            </p>
          </div>
        )}

        <div className="space-y-4">
          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("settings.firebase.projectId")}
            </label>
            <Input
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
              placeholder={t("settings.firebase.projectIdPlaceholder")}
              className="bg-input-background"
            />
          </div>

          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("settings.firebase.serviceAccount")}
              {firebaseSettings?.hasCredential && (
                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 400 }}>
                  {" "}{t("settings.firebase.credentialKeep")}
                </span>
              )}
            </label>
            <textarea
              value={serviceAccountJson}
              onChange={(e) => {
                setServiceAccountJson(e.target.value);
                if (e.target.value.trim()) setClearCredential(false);
              }}
              disabled={clearCredential}
              rows={8}
              placeholder={firebaseSettings?.hasCredential
                ? t("settings.firebase.credentialPlaceholderMasked")
                : t("settings.firebase.credentialPlaceholderEnter")}
              className="w-full rounded-md border border-border bg-input-background px-3 py-2 text-sm font-mono disabled:opacity-50"
            />
          </div>

          {firebaseSettings?.hasCredential && (
            <div className="p-3 rounded-lg bg-surface-light/20">
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={clearCredential}
                  onChange={(e) => {
                    setClearCredential(e.target.checked);
                    if (e.target.checked) setServiceAccountJson("");
                  }}
                  className="w-4 h-4 rounded border-border bg-input-background"
                />
                <div>
                  <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                    {t("settings.firebase.clearCredential")}
                  </span>
                  <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.125rem" }}>
                    {t("settings.firebase.clearCredentialHint")}
                  </p>
                </div>
              </label>
            </div>
          )}
        </div>

        <div className="flex gap-3 mt-6">
          <Button
            onClick={handleSave}
            disabled={saveFirebase.isPending}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {saveFirebase.isPending ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t("common.saving")}
              </>
            ) : (
              <>
                <Save className="w-4 h-4 mr-2" />
                {t("settings.firebase.saveSettings")}
              </>
            )}
          </Button>
          <Button
            variant="outline"
            onClick={() => testFirebase.mutate(undefined)}
            disabled={testFirebase.isPending}
            className="bg-input-background"
          >
            {testFirebase.isPending ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t("settings.firebase.testing")}
              </>
            ) : (
              <>
                <Zap className="w-4 h-4 mr-2" />
                {t("settings.firebase.testCredentials")}
              </>
            )}
          </Button>
        </div>

        <p style={{ fontSize: "0.8125rem", marginTop: "0.75rem" }} className="text-muted-foreground">
          {t("settings.firebase.testHint")}
        </p>

        {testFirebase.data && (
          <div className={`p-3 rounded-lg mt-4 ${testFirebase.data.success ? "bg-success-500/10 border border-success-500/20" : "bg-error-500/10 border border-error-500/20"}`}>
            <p style={{ fontSize: "0.875rem", color: testFirebase.data.success ? "#22C55E" : "#EF4444" }}>
              {testFirebase.data.message}
            </p>
          </div>
        )}
      </Card>
    </div>
  );
}
