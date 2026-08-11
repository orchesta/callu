import { useState } from "react";
import { t } from "@/shared/locales/i18n";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import { Radio, Webhook } from "lucide-react";
import { PageHeader } from "@/shared/components/page-header";
import { ApplicationEndpoints } from "./endpoints";
import { ApplicationTemplates } from "./templates";

export function ApplicationsPage() {
  const [activeTab, setActiveTab] = useState("endpoints");

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title={t("applications.title")}
        subtitle={t("applications.subtitle")}
      />

      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList className="bg-card/80 backdrop-blur-sm border border-border">
          <TabsTrigger value="endpoints">
            <Radio className="w-4 h-4 mr-2" />
            {t("applications.tabEndpoints")}
          </TabsTrigger>
          <TabsTrigger value="templates">
            <Webhook className="w-4 h-4 mr-2" />
            {t("applications.tabTemplates")}
          </TabsTrigger>
        </TabsList>

        <TabsContent value="endpoints" className="space-y-6">
          <ApplicationEndpoints />
        </TabsContent>

        <TabsContent value="templates" className="space-y-6">
          <ApplicationTemplates />
        </TabsContent>
      </Tabs>
    </div>
  );
}
