import { useState } from "react";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { FlaskConical, Trash2, Webhook } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import {
  useWebhookTemplates,
  useDeleteWebhookTemplate,
  useTestWebhookTemplate,
} from "../hooks/use-webhook-templates";
import type { WebhookTemplateDto } from "../types/webhook-template.types";

export function WebhookTemplatesSettings() {
  const { data: templates, isLoading, error } = useWebhookTemplates();
  const deleteTemplate = useDeleteWebhookTemplate();
  const testTemplate = useTestWebhookTemplate();

  const [testing, setTesting] = useState<WebhookTemplateDto | null>(null);
  const [payload, setPayload] = useState("");
  const [deleting, setDeleting] = useState<WebhookTemplateDto | null>(null);

  const openTest = (template: WebhookTemplateDto) => {
    testTemplate.reset();
    setPayload(template.samplePayload ?? "");
    setTesting(template);
  };

  if (isLoading) return <LoadingState message={t("common.loading")} />;

  if (error) {
    return (
      <ErrorState
        title={t("webhookTemplates.title")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <>
      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <div className="mb-4">
          <h3 className="text-lg font-semibold">{t("webhookTemplates.title")}</h3>
          <p className="text-sm text-muted-foreground">{t("webhookTemplates.subtitle")}</p>
        </div>

        {templates && templates.length > 0 ? (
          <div className="space-y-3">
            {templates.map((template) => (
              <div
                key={template.id}
                className="flex flex-col gap-3 rounded-lg border border-border p-4 sm:flex-row sm:items-center sm:justify-between"
              >
                <div className="min-w-0">
                  <div className="mb-1 flex flex-wrap items-center gap-2">
                    <span className="font-semibold">{template.name}</span>
                    {template.isBuiltIn && (
                      <Badge variant="outline">{t("webhookTemplates.builtIn")}</Badge>
                    )}
                    {!template.isActive && (
                      <Badge className="bg-muted text-muted-foreground">
                        {t("webhookTemplates.inactive")}
                      </Badge>
                    )}
                  </div>
                  {template.description && (
                    <p className="text-sm text-muted-foreground">{template.description}</p>
                  )}
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("webhookTemplates.language")}: {template.dataLanguage} ·{" "}
                    {t("webhookTemplates.usedBy", { count: String(template.usageCount) })}
                  </p>
                </div>

                <div className="flex flex-shrink-0 items-center gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    className="bg-input-background"
                    aria-label={t("webhookTemplates.testAria", { name: template.name })}
                    onClick={() => openTest(template)}
                  >
                    <FlaskConical className="mr-2 h-4 w-4" />
                    {t("common.test")}
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-error-400"
                    aria-label={t("webhookTemplates.deleteAria", { name: template.name })}
                    disabled={template.isBuiltIn}
                    title={template.isBuiltIn ? t("webhookTemplates.builtInLocked") : undefined}
                    onClick={() => setDeleting(template)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <EmptyState
            icon={Webhook}
            title={t("webhookTemplates.empty")}
            description={t("webhookTemplates.emptyHint")}
          />
        )}
      </Card>

      <Dialog open={!!testing} onOpenChange={(open) => !open && setTesting(null)}>
        <DialogContent className="bg-card border-border sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>{t("webhookTemplates.testTitle")}</DialogTitle>
            <DialogDescription>{t("webhookTemplates.testDescription")}</DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            <div>
              <label htmlFor="wt-payload" className="mb-2 block text-xs font-semibold text-muted-foreground">
                {t("webhookTemplates.samplePayload")}
              </label>
              <Textarea
                id="wt-payload"
                rows={8}
                value={payload}
                onChange={(e) => setPayload(e.target.value)}
                className="bg-input-background font-mono text-xs"
              />
            </div>

            {testTemplate.data && (
              <div className="rounded-md border border-border p-3">
                {testTemplate.data.success ? (
                  <>
                    <p className="mb-2 text-xs font-semibold text-muted-foreground">
                      {t("webhookTemplates.mapped")}
                    </p>
                    <dl className="space-y-1 text-sm">
                      {Object.entries(testTemplate.data.mappedFields).map(([field, value]) => (
                        <div key={field} className="flex gap-2">
                          <dt className="w-40 flex-shrink-0 font-mono text-xs text-muted-foreground">
                            {field}
                          </dt>
                          <dd className={value ? "" : "text-muted-foreground italic"}>
                            {value ?? t("webhookTemplates.unmapped")}
                          </dd>
                        </div>
                      ))}
                    </dl>
                  </>
                ) : (
                  <p className="text-sm text-error-400">
                    {testTemplate.data.errorMessage || t("webhookTemplates.testFailed")}
                  </p>
                )}
              </div>
            )}
          </div>

          <DialogFooter>
            <Button variant="outline" className="bg-input-background" onClick={() => setTesting(null)}>
              {t("common.close")}
            </Button>
            <Button
              disabled={!payload.trim() || testTemplate.isPending}
              onClick={() =>
                testing && testTemplate.mutate({ id: testing.id, samplePayload: payload })
              }
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {testTemplate.isPending ? t("webhookTemplates.running") : t("webhookTemplates.run")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("webhookTemplates.deleteTitle")}
        message={t("webhookTemplates.deleteMsg", { name: deleting?.name ?? "" })}
        warning={
          deleting && deleting.usageCount > 0
            ? t("webhookTemplates.deleteWarnUsed", { count: String(deleting.usageCount) })
            : t("webhookTemplates.deleteWarn")
        }
        isLoading={deleteTemplate.isPending}
        onConfirm={() => {
          if (!deleting) return;
          deleteTemplate.mutate(deleting.id, { onSettled: () => setDeleting(null) });
        }}
      />
    </>
  );
}
