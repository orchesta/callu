import { useState } from "react";
import { t } from "@/shared/locales/i18n";
import { useParams, useNavigate } from "react-router";
import { Button } from "@/shared/components/ui/button";

import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";

import {
  ArrowLeft,
  Cloud,
  Settings,
  CheckCircle,
  XCircle,
  AlertCircle,
  Users,
  RefreshCw,
  Wand2,
  Info,
} from "lucide-react";

import {
  useVoxAccountInfo,
  useVoxStatus,
  useVoxProvision,
  useVoxSyncUsers,
} from "../hooks/use-voximplant";
import type {
  VoxProvisionResult,
  VoxUserSyncResult,
} from "../types/voximplant.types";

export function VoximplantManagement() {
  const { id: providerId = "" } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: accountInfo, isLoading, refetch, isRefetching } = useVoxAccountInfo(providerId);
  const { data: provStatus, refetch: refetchStatus } = useVoxStatus(providerId);
  const provisionMutation = useVoxProvision();
  const syncUsersMutation = useVoxSyncUsers();

  const [lastSyncResult, setLastSyncResult] = useState<VoxUserSyncResult | null>(null);
  const [lastProvisionResult, setLastProvisionResult] = useState<VoxProvisionResult | null>(null);

  const resources = provStatus?.resources ?? [];
  const isFullyProvisioned = provStatus?.isProvisioned ?? false;
  const hasIssues = resources.some((r) => !r.exists) || (provStatus?.issues?.length ?? 0) > 0;
  const isProvisioning = provisionMutation.isPending;
  const isSyncing = syncUsersMutation.isPending;
  const isRefreshing = isRefetching;

  const handleRefresh = () => {
    refetch();
    refetchStatus();
  };

  const handleProvision = async () => {
    try {
      const result = await provisionMutation.mutateAsync(providerId);
      setLastProvisionResult(result);
      refetchStatus();
    } catch {
      /* empty */
    }
  };

  const handleSyncUsers = async () => {
    try {
      const result = await syncUsersMutation.mutateAsync({ providerId });
      setLastSyncResult(result);
    } catch {
      /* empty */
    }
  };

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[400px]">
        <div className="w-6 h-6 border-2 border-brand-500/30 border-t-brand-500 rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="icon"
            onClick={() => navigate("/settings/communications")}
            className="bg-input-background"
          >
            <ArrowLeft className="w-5 h-5" />
          </Button>
          <div>
            <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>
              {t("voximplant.management")}
            </h1>
            <p
              style={{
                fontSize: "0.875rem",
                color: "#94A3B8",
                marginTop: "0.25rem",
              }}
            >
              {t("voximplant.provisioningStatusAndSync")}
            </p>
          </div>
        </div>
        <Button
          onClick={handleRefresh}
          disabled={isRefreshing}
          variant="outline"
          className="bg-input-background"
        >
          <RefreshCw className={`w-4 h-4 mr-2 ${isRefreshing ? "animate-spin" : ""}`} />
          {t("common.refresh")}
        </Button>
      </div>

      <Card className="p-6 bg-gradient-to-br from-brand-500/10 via-indigo-500/5 to-transparent border-2 border-brand-500/20">
        <div className="flex items-start gap-4">
          <div className="w-12 h-12 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
            <Cloud className="w-6 h-6 text-brand-500" />
          </div>
          <div className="flex-1">
            <h3
              style={{
                fontSize: "1.25rem",
                fontWeight: 600,
                marginBottom: "0.5rem",
              }}
            >
              {accountInfo?.accountName ?? 'Voximplant Account'}
            </h3>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginBottom: "1rem" }}>
              Account ID: {accountInfo?.accountId ?? '—'} • {accountInfo?.accountEmail ?? '—'}
            </p>
            <div className="flex items-center gap-4">
              <div>
                <p
                  style={{
                    fontSize: "1.5rem",
                    fontWeight: 700,
                    color: "#3E7BFA",
                  }}
                  className="glow-text"
                >
                  ${accountInfo?.balance?.toFixed(2) ?? '0.00'} {accountInfo?.currency ?? 'USD'}
                </p>
                <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>{t("voximplant.accountBalance")}</p>
              </div>
              <div className="flex items-center gap-2">
                <div
                  className={`w-2 h-2 rounded-full ${accountInfo?.active ? "bg-success-500 animate-pulse" : "bg-error-500"
                    }`}
                />
                <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                  {accountInfo?.active ? t("common.active") : t("common.inactive")}
                </span>
              </div>
            </div>
          </div>
        </div>
      </Card>

      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <div className="flex items-start gap-4 mb-6">
          <div className="w-10 h-10 rounded-lg bg-purple-500/10 flex items-center justify-center flex-shrink-0">
            <Settings className="w-5 h-5 text-purple-500" />
          </div>
          <div className="flex-1">
            <div className="flex items-center justify-between mb-2">
              <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("voximplant.provisioning")}</h3>
              {isFullyProvisioned ? (
                <Badge className="bg-success-500/10 text-success-500 border-success-500/20 border">
                  ✓ {t("voximplant.fullyProvisioned")}
                </Badge>
              ) : (
                <Badge className="bg-warning-500/10 text-warning-500 border-warning-500/20 border">
                  ⚠ {t("voximplant.notProvisioned")}
                </Badge>
              )}
            </div>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("voximplant.autoProvisionDesc")}
            </p>
          </div>
          <Button
            onClick={handleProvision}
            disabled={isProvisioning}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {isProvisioning ? (
              <>
                <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                {t("voximplant.provisioning")}...
              </>
            ) : (
              <>
                <Wand2 className="w-4 h-4 mr-2" />
                {isFullyProvisioned ? t("voximplant.reProvision") : t("voximplant.provision")}
              </>
            )}
          </Button>
        </div>

        {resources.length > 0 ? (
          <div className="space-y-3">
            {resources.map((resource, idx) => {
              return (
                <div
                  key={idx}
                  className={`flex items-center gap-4 p-4 rounded-lg border-2 transition-colors ${resource.exists
                    ? "border-success-500/20 bg-success-500/5"
                    : "border-muted/20 bg-muted/5"
                    }`}
                >
                  <div
                    className={`w-8 h-8 rounded-full flex items-center justify-center ${resource.exists ? "bg-success-500/10" : "bg-muted/10"
                      }`}
                  >
                    {resource.exists ? (
                      <CheckCircle className="w-5 h-5 text-success-500" />
                    ) : (
                      <XCircle className="w-5 h-5 text-muted-foreground" />
                    )}
                  </div>
                  <div className="flex-1 min-w-0">
                    <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                      {resource.name}
                    </p>
                    <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                      {resource.type.charAt(0).toUpperCase() + resource.type.slice(1)}
                      {resource.resourceId && ` • ID: ${resource.resourceId}`}
                    </p>
                  </div>
                  <Badge
                    className={`border ${resource.exists
                      ? "bg-success-500/10 text-success-500 border-success-500/20"
                      : "bg-muted/10 text-muted-foreground border-muted/20"
                      }`}
                  >
                    {resource.exists ? t("voximplant.ready") : t("voximplant.missing")}
                  </Badge>
                </div>
              );
            })}
          </div>
        ) : (
          <div className="text-center py-12">
            <Settings className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
            <p
              style={{
                fontSize: "0.9375rem",
                fontWeight: 600,
                marginBottom: "0.5rem",
              }}
            >
              {t("voximplant.noProvisioningData")}
            </p>
            <p
              style={{
                fontSize: "0.8125rem",
                color: "#94A3B8",
                marginBottom: "1.5rem",
              }}
            >
              {t("voximplant.clickProvisionDesc")}
            </p>
          </div>
        )}

        {hasIssues && (
          <div className="mt-4 p-4 rounded-lg bg-warning-500/10 border border-warning-500/20">
            <div className="flex gap-3">
              <AlertCircle className="w-5 h-5 text-warning-500 flex-shrink-0 mt-0.5" />
              <div>
                <p style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                  {t("voximplant.someResourcesMissing")}
                </p>
                <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                  {t("voximplant.voiceCallsMayNotWork")}
                </p>
              </div>
            </div>
          </div>
        )}
      </Card>

      {lastProvisionResult && (
        <Card className="p-5 bg-card/80 backdrop-blur-sm border-border">
          <div className="flex items-start gap-3">
            {!lastProvisionResult.error ? (
              <CheckCircle className="w-5 h-5 text-success-500 flex-shrink-0 mt-0.5" />
            ) : (
              <XCircle className="w-5 h-5 text-error-500 flex-shrink-0 mt-0.5" />
            )}
            <div className="flex-1">
              <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "1rem" }}>
                {t("voximplant.lastProvisioningResult")}
              </p>
              {!lastProvisionResult.error ? (
                <div className="space-y-3">
                  {lastProvisionResult.createdResources.length > 0 && (
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#4ade80", marginBottom: "0.5rem" }}>
                        {t("voximplant.created")}:
                      </p>
                      <div className="flex flex-wrap gap-2">
                        {lastProvisionResult.createdResources.map((r, i) => (
                          <Badge key={i} className="bg-success-500/10 text-success-500 border-success-500/20 border">
                            <CheckCircle className="w-3 h-3 mr-1" />
                            {r}
                          </Badge>
                        ))}
                      </div>
                    </div>
                  )}
                  {lastProvisionResult.existingResources.length > 0 && (
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", marginBottom: "0.5rem" }}>
                        {t("voximplant.unchanged")}:
                      </p>
                      <div className="flex flex-wrap gap-2">
                        {lastProvisionResult.existingResources.map((r, i) => (
                          <Badge key={i} className="bg-muted/10 text-muted-foreground border-muted/20 border">
                            <Info className="w-3 h-3 mr-1" />
                            {r}
                          </Badge>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              ) : (
                <p style={{ fontSize: "0.875rem", color: "#FF4D4D" }}>
                  {lastProvisionResult.error}
                </p>
              )}
            </div>
          </div>
        </Card>
      )}

      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <div className="flex items-start gap-4 mb-6">
          <div className="w-10 h-10 rounded-lg bg-sky-500/10 flex items-center justify-center flex-shrink-0">
            <Users className="w-5 h-5 text-sky-500" />
          </div>
          <div className="flex-1">
            <div className="flex items-center justify-between mb-2">
              <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>
                {t("voximplant.userSynchronization")}
              </h3>
              {(lastSyncResult && lastSyncResult.errors.length === 0) || provStatus?.usersInSync ? (
                <Badge className="bg-success-500/10 text-success-500 border-success-500/20 border">
                  ✓ {t("voximplant.inSync")}
                </Badge>
              ) : (
                <Badge className="bg-warning-500/10 text-warning-500 border-warning-500/20 border">
                  ⚠ {t("voximplant.mayNeedSync")}
                </Badge>
              )}
            </div>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("voximplant.syncCalluAppUsers")}
            </p>
          </div>
          <Button
            onClick={handleSyncUsers}
            disabled={isSyncing || !isFullyProvisioned}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {isSyncing ? (
              <>
                <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                {t("voximplant.syncing")}
              </>
            ) : (
              <>
                <RefreshCw className="w-4 h-4 mr-2" />
                {t("voximplant.syncUsers")}
              </>
            )}
          </Button>
        </div>

        {lastSyncResult && (
          <div className="grid grid-cols-3 gap-4 mb-4">
            <div className="p-4 rounded-lg bg-surface-light/20 text-center">
              <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{lastSyncResult.usersCreated}</p>
              <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                {t("voximplant.created")}
              </p>
            </div>
            <div className="p-4 rounded-lg bg-surface-light/20 text-center">
              <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{lastSyncResult.usersUnchanged}</p>
              <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                {t("voximplant.unchanged")}
              </p>
            </div>
            <div className="p-4 rounded-lg bg-surface-light/20 text-center">
              <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{lastSyncResult.usersDeleted}</p>
              <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                {t("voximplant.deleted")}
              </p>
            </div>
          </div>
        )}

        {!isFullyProvisioned && (
          <p
            style={{
              fontSize: "0.875rem",
              color: "#94A3B8",
              textAlign: "center",
              padding: "0.75rem",
            }}
          >
            {t("voximplant.provisioningRequired")}
          </p>
        )}
      </Card>
    </div>
  );
}

