import { useState, useEffect, useId } from "react";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Card } from "@/shared/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { Save, Loader2, AlertTriangle } from "lucide-react";
import { isUnreachableBaseUrl } from "@/shared/utils/base-url";
import {
  useOrganizationSettings,
  useUpdateOrganization,
  useTimezones,
} from "../hooks/use-settings";

export function GeneralSettings() {
  const formId = useId();
  const { data: orgSettings } = useOrganizationSettings();
  const updateOrg = useUpdateOrganization();
  const { data: timezones } = useTimezones();

  const [organizationName, setOrganizationName] = useState("");
  const [timezone, setTimezone] = useState("UTC");
  const [culture, setCulture] = useState("en-US");
  const [baseUrl, setBaseUrl] = useState("");
  const [emailNotificationsEnabled, setEmailNotificationsEnabled] = useState(true);

  useEffect(() => {
    if (orgSettings) {
      setOrganizationName(orgSettings.organizationName ?? "");
      setTimezone(orgSettings.defaultTimezone ?? "UTC");
      setCulture(orgSettings.defaultCulture ?? "en-US");
      setBaseUrl(orgSettings.baseUrl ?? "");
      setEmailNotificationsEnabled(orgSettings.emailNotificationsEnabled ?? true);
    }
  }, [orgSettings]);

  const handleSaveOrg = () => {
    updateOrg.mutate({
      organizationName,
      defaultTimezone: timezone,
      defaultCulture: culture,
      emailNotificationsEnabled,
      baseUrl,
    });
  };

  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
      <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
        {t("settings.org.title")}
      </h3>
      <div className="space-y-4">
        <div>
          <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
            {t("settings.org.name")}
          </label>
          <Input
            value={organizationName}
            onChange={(e) => setOrganizationName(e.target.value)}
            className="bg-input-background"
          />
        </div>

        <div>
          <label htmlFor={`${formId}-timezone`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
            {t("settings.org.timezone")}
          </label>
          <Select value={timezone} onValueChange={setTimezone}>
            <SelectTrigger id={`${formId}-timezone`} className="bg-input-background">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {timezones && timezones.length > 0 ? (
                timezones.filter((tz) => tz.id).map((tz) => (
                  <SelectItem key={tz.id} value={tz.id}>
                    {tz.displayName}
                  </SelectItem>
                ))
              ) : (
                <>
                  <SelectItem value="UTC">UTC</SelectItem>
                  <SelectItem value="America/New_York">Eastern Time (ET)</SelectItem>
                  <SelectItem value="America/Chicago">Central Time (CT)</SelectItem>
                  <SelectItem value="America/Los_Angeles">Pacific Time (PT)</SelectItem>
                  <SelectItem value="Europe/London">London (GMT)</SelectItem>
                  <SelectItem value="Europe/Istanbul">Istanbul (TRT)</SelectItem>
                </>
              )}
            </SelectContent>
          </Select>
        </div>

        <div>
          <label htmlFor={`${formId}-culture`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
            {t("settings.org.culture")}
          </label>
          <Select value={culture} onValueChange={setCulture}>
            <SelectTrigger id={`${formId}-culture`} className="bg-input-background">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="en-US">English (US)</SelectItem>
              <SelectItem value="en-GB">English (UK)</SelectItem>
              <SelectItem value="tr-TR">Turkce</SelectItem>
              <SelectItem value="de-DE">Deutsch</SelectItem>
              <SelectItem value="fr-FR">Francais</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div>
          <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
            {t("settings.org.baseUrl")}
          </label>
          <Input
            value={baseUrl}
            onChange={(e) => setBaseUrl(e.target.value)}
            placeholder={t("settings.general.publicBaseUrlPlaceholder")}
            className="bg-input-background"
          />
          <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.5rem" }}>
            {t("settings.org.baseUrlHint")}
          </p>
          {isUnreachableBaseUrl(baseUrl) && (
            <p
              role="alert"
              className="mt-2 flex items-start gap-2 rounded-lg border border-warning-500/30 bg-warning-500/10 p-2.5 text-sm text-foreground"
            >
              <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0 text-warning-500" />
              <span>{t("settings.org.baseUrlUnreachable")}</span>
            </p>
          )}
        </div>

        <div className="flex items-center gap-2 p-3 rounded-lg bg-surface-light/20">
          <input
            type="checkbox"
            id="email-notifications"
            checked={emailNotificationsEnabled}
            onChange={(e) => setEmailNotificationsEnabled(e.target.checked)}
            className="w-4 h-4 rounded border-border bg-input-background"
          />
          <label htmlFor="email-notifications" style={{ fontSize: "0.875rem", cursor: "pointer" }}>
            {t("settings.org.emailNotifications")}
          </label>
        </div>
      </div>

      <Button
        onClick={handleSaveOrg}
        disabled={updateOrg.isPending}
        className="bg-brand-500 hover:bg-brand-600 text-white mt-6"
      >
        {updateOrg.isPending ? (
          <>
            <Loader2 className="w-4 h-4 mr-2 animate-spin" />
            {t("common.saving")}
          </>
        ) : (
          <>
            <Save className="w-4 h-4 mr-2" />
            {t("common.saveChanges")}
          </>
        )}
      </Button>
    </Card>
  );
}
