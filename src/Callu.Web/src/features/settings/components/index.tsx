import { useState } from "react";
import { t } from "@/shared/locales/i18n";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import { Globe, Key, Mail, AlertTriangle, Zap, Bell } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { PageHeader } from "@/shared/components/page-header";
import { useOrganizationSettings, useSmtpSettings, useWebhookApiKeys } from "../hooks/use-settings";
import { GeneralSettings } from "./general-settings";
import { ApiKeysSettings } from "./api-keys-settings";
import { EmailSettings } from "./email-settings";
import { DangerZoneSettings } from "./danger-zone-settings";
import { AlertRulesSettings } from "./alert-rules";
import { NotificationChannelsSettings } from "./notification-channels";

export function SettingsHub() {
  const [activeTab, setActiveTab] = useState("general");

  const { isLoading: isLoadingOrg } = useOrganizationSettings();
  const { isLoading: isLoadingSmtp } = useSmtpSettings();
  const { isLoading: isLoadingKeys } = useWebhookApiKeys();

  if (isLoadingOrg && isLoadingSmtp && isLoadingKeys) {
    return <LoadingState message={t("settings.hubLoading")} />;
  }

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title={t("settings.title")}
        subtitle={t("settings.subtitle")}
      />

      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList className="bg-card/80 backdrop-blur-sm border border-border">
          <TabsTrigger value="general">
            <Globe className="w-4 h-4 mr-2" />
            {t("settings.tabs.general")}
          </TabsTrigger>
          <TabsTrigger value="api-keys">
            <Key className="w-4 h-4 mr-2" />
            {t("settings.tabs.apiKeys")}
          </TabsTrigger>
          <TabsTrigger value="alert-rules">
            <Zap className="w-4 h-4 mr-2" />
            {t("settings.tabs.alertRules")}
          </TabsTrigger>
          <TabsTrigger value="notifications">
            <Bell className="w-4 h-4 mr-2" />
            {t("settings.tabs.notifications")}
          </TabsTrigger>
          <TabsTrigger value="email">
            <Mail className="w-4 h-4 mr-2" />
            {t("settings.tabs.smtp")}
          </TabsTrigger>
          <TabsTrigger value="danger">
            <AlertTriangle className="w-4 h-4 mr-2" />
            {t("settings.danger.title")}
          </TabsTrigger>
        </TabsList>

        <TabsContent value="general" className="space-y-6">
          <GeneralSettings />
        </TabsContent>

        <TabsContent value="api-keys" className="space-y-6">
          <ApiKeysSettings />
        </TabsContent>

        <TabsContent value="alert-rules" className="space-y-6">
          <AlertRulesSettings />
        </TabsContent>

        <TabsContent value="notifications" className="space-y-6">
          <NotificationChannelsSettings />
        </TabsContent>

        <TabsContent value="email" className="space-y-6">
          <EmailSettings />
        </TabsContent>

        <TabsContent value="danger" className="space-y-6">
          <DangerZoneSettings />
        </TabsContent>
      </Tabs>
    </div>
  );
}
