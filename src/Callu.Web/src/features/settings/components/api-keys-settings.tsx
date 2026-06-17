import { Link } from "react-router";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Key, Server, ExternalLink, Loader2, AlertCircle, ShieldCheck, ShieldAlert } from "lucide-react";
import { useWebhookApiKeys } from "../hooks/use-settings";

/**
 * Read-only inventory of webhook API keys (one row per service that has one).
 *
 * This page used to manage a standalone ApiKey entity, but nothing in the
 * codebase actually accepts those keys for auth — the only API keys Callu
 * really uses are the per-service webhook keys, and those are created/rotated
 * on the service detail page where the plaintext is surfaced exactly once.
 * Re-creating that flow here would just give admins a second place to lose
 * the plaintext, so this page lists what exists and links out for rotation.
 */
export function ApiKeysSettings() {
    const { data, isLoading, isError } = useWebhookApiKeys();
    const rows = data ?? [];

    return (
        <div className="space-y-6">
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                <div className="flex items-start gap-3 mb-4">
                    <div className="w-10 h-10 rounded-lg bg-brand-500/10 border border-brand-500/20 flex items-center justify-center flex-shrink-0">
                        <Key className="w-5 h-5 text-brand-500" />
                    </div>
                    <div className="flex-1 min-w-0">
                        <h2 className="text-lg font-semibold">{t("settings.apiKeys.title")}</h2>
                        <p className="text-sm text-muted-foreground mt-1">
                            {t("settings.apiKeys.subtitle")}
                        </p>
                    </div>
                </div>

                <div className="p-3 rounded-lg bg-brand-500/5 border border-brand-500/20 flex gap-3">
                    <AlertCircle className="w-4 h-4 text-brand-500 flex-shrink-0 mt-0.5" />
                    <p className="text-sm text-foreground/90">
                        {t("settings.apiKeys.helpBody")}
                    </p>
                </div>
            </Card>

            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                {isLoading ? (
                    <div className="flex items-center justify-center py-12">
                        <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
                    </div>
                ) : isError ? (
                    <div className="text-center py-12">
                        <AlertCircle className="w-12 h-12 text-error-500 mx-auto mb-3 opacity-50" />
                        <p className="text-sm text-muted-foreground">{t("settings.apiKeys.loadError")}</p>
                    </div>
                ) : rows.length === 0 ? (
                    <div className="text-center py-12">
                        <Key className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                        <p className="text-base font-semibold mb-1">{t("settings.apiKeys.emptyTitle")}</p>
                        <p className="text-sm text-muted-foreground mb-4">{t("settings.apiKeys.emptyBody")}</p>
                        <Link to="/services">
                            <Button variant="outline" className="bg-input-background">
                                <Server className="w-4 h-4 mr-2" />
                                {t("settings.apiKeys.goToServices")}
                            </Button>
                        </Link>
                    </div>
                ) : (
                    <div className="space-y-2">
                        {rows.map((row) => (
                            <div
                                key={row.serviceId}
                                className="flex items-center gap-4 p-3 rounded-lg border border-border bg-surface-light/10 hover:border-border-light transition-colors"
                            >
                                <div className="w-9 h-9 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                                    <Server className="w-4 h-4 text-brand-500" />
                                </div>
                                <div className="flex-1 min-w-0">
                                    <div className="flex items-center gap-2 flex-wrap">
                                        <Link
                                            to={`/services/${row.serviceId}`}
                                            className="text-sm font-semibold hover:text-brand-500 transition-colors truncate"
                                        >
                                            {row.serviceName}
                                        </Link>
                                        {!row.webhookEnabled && (
                                            <Badge className="bg-warning-500/10 text-warning-500 border-warning-500/20 border text-xs">
                                                {t("settings.apiKeys.webhookDisabled")}
                                            </Badge>
                                        )}
                                        {row.hasSignatureSecret ? (
                                            <Badge className="bg-success-500/10 text-success-500 border-success-500/20 border text-xs flex items-center gap-1">
                                                <ShieldCheck className="w-3 h-3" />
                                                {t("settings.apiKeys.hmacOn")}
                                            </Badge>
                                        ) : (
                                            <Badge className="bg-muted/10 text-muted-foreground border-muted/20 border text-xs flex items-center gap-1">
                                                <ShieldAlert className="w-3 h-3" />
                                                {t("settings.apiKeys.hmacOff")}
                                            </Badge>
                                        )}
                                    </div>
                                    <div className="flex items-center gap-3 mt-1 flex-wrap">
                                        <code className="text-xs font-mono px-1.5 py-0.5 rounded bg-input-background text-muted-foreground">
                                            {row.maskedApiKey ?? "—"}
                                        </code>
                                        {row.updatedAt && (
                                            <span className="text-xs text-muted-foreground">
                                                {t("settings.apiKeys.lastUpdated")}{" "}
                                                {new Date(row.updatedAt).toLocaleDateString()}
                                            </span>
                                        )}
                                    </div>
                                </div>
                                <Link to={`/services/${row.serviceId}`}>
                                    <Button variant="outline" size="sm" className="bg-input-background flex-shrink-0">
                                        {t("settings.apiKeys.manageOnService")}
                                        <ExternalLink className="w-3 h-3 ml-2" />
                                    </Button>
                                </Link>
                            </div>
                        ))}
                    </div>
                )}
            </Card>
        </div>
    );
}
