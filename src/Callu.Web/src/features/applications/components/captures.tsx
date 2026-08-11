import { WebhookCaptures } from "@/features/services/components/webhook-captures";

/** Captures page for an integration endpoint; the route :id is the integration id. */
export function ApplicationCaptures() {
  return <WebhookCaptures scope="application" />;
}
