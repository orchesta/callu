import { Card } from "@/shared/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/shared/components/ui/select";
import { Badge } from "@/shared/components/ui/badge";
import { AlertTriangle, Route } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import { useCapabilityRoutes, useSetCapabilityRoute } from "@/features/settings/hooks/use-communications";
import { CAPABILITY_LABEL_KEYS, UNROUTED } from "../utils/capability-routing";

/** Lets each channel be handed to a named provider, so voice and video need not share one. */
export function CapabilityRoutingSection() {
  const { data: routes, isLoading } = useCapabilityRoutes();
  const setRoute = useSetCapabilityRoute();

  if (isLoading) {
    return (
      <Card className="p-6">
        <p className="text-sm text-dim">{t("common.loading")}</p>
      </Card>
    );
  }

  const rows = routes ?? [];

  return (
    <Card className="p-6">
      <div className="mb-1 flex items-center gap-2">
        <Route className="h-4 w-4 text-brand-400" />
        <h3 className="font-medium text-ink">{t("providers.routingTitle")}</h3>
      </div>
      <p className="mb-5 text-sm text-dim">{t("providers.routingDesc")}</p>

      <div className="flex flex-col gap-3">
        {rows.map((route) => {
          const usable = route.candidates.filter((c) => c.isEnabled);
          const pinnedButOff = route.providerId !== null && !route.isProviderEnabled;

          return (
            <div
              key={route.capability}
              className="grid gap-3 rounded-lg border border-border p-3 sm:grid-cols-[180px_1fr]"
            >
              <div className="flex flex-col justify-center">
                <span className="text-sm font-medium text-ink">
                  {t(CAPABILITY_LABEL_KEYS[route.capability] ?? "providers.routingUnknownChannel")}
                </span>
                {route.providerId === null && (
                  <span className="text-xs text-dim">{t("providers.routingAutomatic")}</span>
                )}
              </div>

              <div className="flex flex-col gap-1.5">
                <Select
                  value={route.providerId ?? UNROUTED}
                  onValueChange={(value) =>
                    setRoute.mutate({
                      capability: route.capability,
                      providerId: value === UNROUTED ? null : value,
                    })
                  }
                  disabled={setRoute.isPending || route.candidates.length === 0}
                >
                  <SelectTrigger className="bg-input-background">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={UNROUTED}>{t("providers.routingAutomaticOption")}</SelectItem>
                    {route.candidates.map((candidate) => (
                      <SelectItem key={candidate.providerId} value={candidate.providerId}>
                        {candidate.name}
                        {!candidate.isEnabled && ` — ${t("providers.routingProviderOff")}`}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>

                {route.candidates.length === 0 && (
                  <span className="text-xs text-warning-500">{t("providers.routingCandidateGone")}</span>
                )}

                {pinnedButOff && (
                  <span className="flex items-center gap-1.5 text-xs text-warning-500">
                    <AlertTriangle className="h-3.5 w-3.5" />
                    {t("providers.routingPinnedToDisabled")}
                  </span>
                )}

                {route.providerId !== null && route.isProviderEnabled && usable.length > 1 && (
                  <Badge className="w-fit border border-border bg-surface-light/20 text-xs text-dim">
                    {t("providers.routingOverridesOrder")}
                  </Badge>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </Card>
  );
}
