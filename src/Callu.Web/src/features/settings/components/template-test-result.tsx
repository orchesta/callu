import { t } from "@/shared/locales/i18n";
import type { WebhookTemplateTestResult } from "../types/webhook-template.types";

/** Renders one template dry-run result: the mapped fields, or the failure message. */
export function TemplateTestResultView({ result }: { result: WebhookTemplateTestResult }) {
  return (
    <div className="rounded-md border border-border p-3">
      {result.success ? (
        <>
          <p className="mb-2 text-xs font-semibold text-muted-foreground">
            {t("webhookTemplates.mapped")}
          </p>
          <dl className="space-y-1 text-sm">
            {Object.entries(result.mappedFields).map(([field, value]) => (
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
          {result.errorMessage || t("webhookTemplates.testFailed")}
        </p>
      )}
    </div>
  );
}
