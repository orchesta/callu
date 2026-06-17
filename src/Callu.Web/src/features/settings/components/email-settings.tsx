import { useState, useEffect } from "react";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Card } from "@/shared/components/ui/card";
import { Mail, Save, Eye, EyeOff, Zap, Loader2 } from "lucide-react";
import {
  useSmtpSettings,
  useSaveSmtp,
  useTestSmtpConnection,
  useSendTestEmail,
} from "../hooks/use-settings";

export function EmailSettings() {
  const { data: smtpSettings } = useSmtpSettings();
  const saveSmtp = useSaveSmtp();
  const testConnection = useTestSmtpConnection();
  const sendTestEmail = useSendTestEmail();

  const [smtpHost, setSmtpHost] = useState("");
  const [smtpPort, setSmtpPort] = useState("587");
  const [smtpUsername, setSmtpUsername] = useState("");
  const [smtpPassword, setSmtpPassword] = useState("");
  const [showSmtpPassword, setShowSmtpPassword] = useState(false);
  const [smtpFromAddress, setSmtpFromAddress] = useState("");
  const [smtpFromName, setSmtpFromName] = useState("CalluApp");
  const [smtpUseSsl, setSmtpUseSsl] = useState(true);
  const [testRecipient, setTestRecipient] = useState("");

  useEffect(() => {
    if (smtpSettings) {
      setSmtpHost(smtpSettings.host ?? "");
      setSmtpPort(String(smtpSettings.port ?? 587));
      setSmtpUsername(smtpSettings.username ?? "");
      setSmtpFromAddress(smtpSettings.fromAddress ?? "");
      setSmtpFromName(smtpSettings.fromName ?? "CalluApp");
      setSmtpUseSsl(smtpSettings.enableSsl ?? true);
    }
  }, [smtpSettings]);

  const handleSaveSmtp = () => {
    saveSmtp.mutate({
      host: smtpHost,
      port: parseInt(smtpPort, 10) || 587,
      enableSsl: smtpUseSsl,
      username: smtpUsername || undefined,
      password: smtpPassword || undefined,
      fromAddress: smtpFromAddress,
      fromName: smtpFromName,
    });
  };

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
          {t("settings.smtp.title")}
        </h3>

        {smtpSettings?.isConfigured && (
          <div className="p-3 rounded-lg bg-success-500/10 border border-success-500/20 mb-4">
            <p style={{ fontSize: "0.8125rem", color: "#22C55E" }}>
              {t("settings.smtp.configured")}
              {smtpSettings.lastTestedAt && (
                <> &middot; {t("settings.smtp.lastTested", { date: new Date(smtpSettings.lastTestedAt).toLocaleDateString() })}</>
              )}
            </p>
          </div>
        )}

        <div className="space-y-4">
          <div className="grid grid-cols-3 gap-3">
            <div className="col-span-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("settings.smtp.host")}
              </label>
              <Input
                value={smtpHost}
                onChange={(e) => setSmtpHost(e.target.value)}
                placeholder={t("settings.smtp.hostPlaceholder")}
                className="bg-input-background"
              />
            </div>
            <div>
              <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("settings.smtp.port")}
              </label>
              <Input
                type="number"
                value={smtpPort}
                onChange={(e) => setSmtpPort(e.target.value)}
                className="bg-input-background"
              />
            </div>
          </div>

          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("settings.smtp.username")}
            </label>
            <Input
              value={smtpUsername}
              onChange={(e) => setSmtpUsername(e.target.value)}
              className="bg-input-background"
            />
          </div>

          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("settings.smtp.password")}
              {smtpSettings?.hasPassword && (
                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 400 }}>
                  {" "}{t("settings.smtp.passwordKeep")}
                </span>
              )}
            </label>
            <div className="relative">
              <Input
                type={showSmtpPassword ? "text" : "password"}
                value={smtpPassword}
                onChange={(e) => setSmtpPassword(e.target.value)}
                placeholder={smtpSettings?.hasPassword ? t("settings.smtp.passwordPlaceholderMasked") : t("settings.smtp.passwordPlaceholderEnter")}
                className="bg-input-background pr-10"
              />
              <button
                type="button"
                onClick={() => setShowSmtpPassword(!showSmtpPassword)}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
              >
                {showSmtpPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("settings.smtp.fromAddress")}
            </label>
            <Input
              type="email"
              value={smtpFromAddress}
              onChange={(e) => setSmtpFromAddress(e.target.value)}
              placeholder={t("settings.smtp.fromAddressPlaceholder")}
              className="bg-input-background"
            />
          </div>

          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("settings.smtp.fromName")}
            </label>
            <Input
              value={smtpFromName}
              onChange={(e) => setSmtpFromName(e.target.value)}
              placeholder={t("settings.smtp.fromNamePlaceholder")}
              className="bg-input-background"
            />
          </div>

          <div className="p-3 rounded-lg bg-surface-light/20">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={smtpUseSsl}
                onChange={(e) => setSmtpUseSsl(e.target.checked)}
                className="w-4 h-4 rounded border-border bg-input-background"
              />
              <div>
                <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>{t("settings.smtp.ssl")}</span>
                <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.125rem" }}>
                  {t("settings.smtp.sslHint")}
                </p>
              </div>
            </label>
          </div>
        </div>

        <div className="flex gap-3 mt-6">
          <Button
            onClick={handleSaveSmtp}
            disabled={saveSmtp.isPending}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {saveSmtp.isPending ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t("common.saving")}
              </>
            ) : (
              <>
                <Save className="w-4 h-4 mr-2" />
                {t("settings.smtp.saveSettings")}
              </>
            )}
          </Button>
          <Button
            variant="outline"
            onClick={() => testConnection.mutate(undefined)}
            disabled={testConnection.isPending}
            className="bg-input-background"
          >
            {testConnection.isPending ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t("settings.smtp.testing")}
              </>
            ) : (
              <>
                <Zap className="w-4 h-4 mr-2" />
                {t("settings.smtp.testConnection")}
              </>
            )}
          </Button>
        </div>

        {testConnection.data && (
          <div className={`p-3 rounded-lg mt-4 ${testConnection.data.success ? "bg-success-500/10 border border-success-500/20" : "bg-error-500/10 border border-error-500/20"}`}>
            <p style={{ fontSize: "0.875rem", color: testConnection.data.success ? "#22C55E" : "#EF4444" }}>
              {testConnection.data.message}
            </p>
          </div>
        )}
      </Card>

      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
          {t("settings.smtp.testEmail")}
        </h3>

        <div className="space-y-4">
          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("settings.smtp.recipient")}
            </label>
            <Input
              type="email"
              value={testRecipient}
              onChange={(e) => setTestRecipient(e.target.value)}
              placeholder={t("settings.smtp.testRecipientPlaceholder")}
              className="bg-input-background"
            />
          </div>

          <Button
            onClick={() => { if (testRecipient) sendTestEmail.mutate(testRecipient); }}
            disabled={!testRecipient || sendTestEmail.isPending}
            className="w-full bg-success-600 hover:bg-success-700 text-white"
          >
            {sendTestEmail.isPending ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t("settings.smtp.sending")}
              </>
            ) : (
              <>
                <Mail className="w-4 h-4 mr-2" />
                {t("settings.smtp.testEmail")}
              </>
            )}
          </Button>

          {sendTestEmail.data && (
            <div className={`p-3 rounded-lg ${sendTestEmail.data.success ? "bg-success-500/10 border border-success-500/20" : "bg-error-500/10 border border-error-500/20"}`}>
              <p style={{ fontSize: "0.875rem", color: sendTestEmail.data.success ? "#22C55E" : "#EF4444" }}>
                {sendTestEmail.data.message}
              </p>
            </div>
          )}

          <div className="p-3 rounded-lg bg-brand-500/5 border border-brand-500/20">
            <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
              {t("settings.smtp.testHint")}
            </p>
          </div>
        </div>
      </Card>
    </div>
  );
}
