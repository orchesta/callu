import { useState, useMemo } from "react";
import { t } from "@/shared/locales/i18n";
import { Card } from "@/shared/components/ui/card";
import { Badge } from "@/shared/components/ui/badge";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/shared/components/ui/select";
import {
    TrendingUp,
    Clock,
    BarChart3,
    Activity,
    Shield,
    Users,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import {
    useIncidentTrends,
    useMttMetrics,
    useServiceUptime,
    useTeamPerformance,
    useSeverityDistribution,
} from "../hooks/use-reports";

const PRESET_RANGES = [
    { value: "7d", label: t("reports.last7d"), days: 7 },
    { value: "14d", label: t("reports.last14d"), days: 14 },
    { value: "30d", label: t("reports.last30d"), days: 30 },
    { value: "90d", label: t("reports.last90d"), days: 90 },
] as const;

const SEVERITY_COLORS: Record<string, string> = {
    Critical: "#EF4444",
    High: "#F97316",
    Medium: "#EAB308",
    Low: "#3B82F6",
    Info: "#94A3B8",
};

export function ReportsPage() {
    const [range, setRange] = useState("30d");
    const [groupBy, setGroupBy] = useState("day");

    const { from, to } = useMemo(() => {
        const preset = PRESET_RANGES.find((r) => r.value === range) ?? PRESET_RANGES[2];
        const to = new Date();
        const from = new Date();
        from.setDate(from.getDate() - preset.days);
        return { from, to };
    }, [range]);

    const { data: trends, isLoading: trendsLoading } = useIncidentTrends(from, to, groupBy);
    const { data: mtt, isLoading: mttLoading } = useMttMetrics(from, to);
    const { data: uptime, isLoading: uptimeLoading } = useServiceUptime(from, to);
    const { data: teamPerf, isLoading: teamLoading } = useTeamPerformance(from, to);
    const { data: severity, isLoading: sevLoading } = useSeverityDistribution(from, to);

    const anyLoading = trendsLoading || mttLoading || uptimeLoading || teamLoading || sevLoading;

    const totalIncidents = trends?.reduce((s, t) => s + t.count, 0) ?? 0;
    const totalCritical = trends?.reduce((s, t) => s + t.critical, 0) ?? 0;
    const latestMtta = mtt && mtt.length > 0 ? mtt[mtt.length - 1].mttaMinutes : 0;
    const latestMttr = mtt && mtt.length > 0 ? mtt[mtt.length - 1].mttrMinutes : 0;

    return (
        <div className="p-6 space-y-6">
            <div className="flex items-center justify-between">
                <div>
                    <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>{t("reports.title")}</h1>
                    <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                        {t("reports.subtitle")}
                    </p>
                </div>
                <div className="flex items-center gap-3">
                    <Select value={groupBy} onValueChange={setGroupBy}>
                        <SelectTrigger className="bg-input-background w-[120px]">
                            <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value="day">{t("reports.daily")}</SelectItem>
                            <SelectItem value="week">{t("reports.weekly")}</SelectItem>
                            <SelectItem value="month">{t("reports.monthly")}</SelectItem>
                        </SelectContent>
                    </Select>
                    <Select value={range} onValueChange={setRange}>
                        <SelectTrigger className="bg-input-background w-[160px]">
                            <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                            {PRESET_RANGES.map((r) => (
                                <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                </div>
            </div>

            {anyLoading && (
                <LoadingState message={t("reports.loading")} />
            )}

            {!anyLoading && (
                <>
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                        <Card className="p-5 bg-card/80 backdrop-blur-sm border-border">
                            <div className="flex items-center justify-between mb-2">
                                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 600, textTransform: "uppercase" }}>
                                    {t("reports.totalIncidents")}
                                </span>
                                <BarChart3 className="w-4 h-4 text-brand-400" />
                            </div>
                            <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{totalIncidents}</p>
                            <p style={{ fontSize: "0.75rem", color: "#64748B", marginTop: "0.25rem" }}>
                                {totalCritical} {t("reports.critical")}
                            </p>
                        </Card>

                        <Card className="p-5 bg-card/80 backdrop-blur-sm border-border">
                            <div className="flex items-center justify-between mb-2">
                                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 600, textTransform: "uppercase" }}>
                                    {t("dashboard.metricMtta")}
                                </span>
                                <Clock className="w-4 h-4 text-warning-400" />
                            </div>
                            <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>
                                {latestMtta < 60 ? `${Math.round(latestMtta)}m` : `${(latestMtta / 60).toFixed(1)}h`}
                            </p>
                            <p style={{ fontSize: "0.75rem", color: "#64748B", marginTop: "0.25rem" }}>
                                {t("dashboard.tooltipMtta")}
                            </p>
                        </Card>

                        <Card className="p-5 bg-card/80 backdrop-blur-sm border-border">
                            <div className="flex items-center justify-between mb-2">
                                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 600, textTransform: "uppercase" }}>
                                    {t("dashboard.metricMttr")}
                                </span>
                                <Activity className="w-4 h-4 text-success-400" />
                            </div>
                            <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>
                                {latestMttr < 60 ? `${Math.round(latestMttr)}m` : `${(latestMttr / 60).toFixed(1)}h`}
                            </p>
                            <p style={{ fontSize: "0.75rem", color: "#64748B", marginTop: "0.25rem" }}>
                                {t("dashboard.tooltipMttr")}
                            </p>
                        </Card>

                        <Card className="p-5 bg-card/80 backdrop-blur-sm border-border">
                            <div className="flex items-center justify-between mb-2">
                                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 600, textTransform: "uppercase" }}>
                                    {t("reports.servicesTracked")}
                                </span>
                                <Shield className="w-4 h-4 text-info-400" />
                            </div>
                            <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{uptime?.length ?? 0}</p>
                            <p style={{ fontSize: "0.75rem", color: "#64748B", marginTop: "0.25rem" }}>
                                {t("reports.withUptimeMonitoring")}
                            </p>
                        </Card>
                    </div>

                    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                        <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                            <TrendingUp className="w-5 h-5 inline-block mr-2 text-brand-400" />
                            {t("reports.incidentTrends")}
                        </h3>
                        {!trends || trends.length === 0 ? (
                            <p style={{ fontSize: "0.875rem", color: "#94A3B8", textAlign: "center", padding: "2rem 0" }}>
                                {t("reports.noIncidentData")}
                            </p>
                        ) : (
                            <div className="space-y-2">
                                <div className="flex items-end gap-1" style={{ height: "160px" }}>
                                    {trends.map((point, i) => {
                                        const maxCount = Math.max(...trends.map((t) => t.count), 1);
                                        const height = (point.count / maxCount) * 100;
                                        return (
                                            <div
                                                key={i}
                                                className="flex-1 group relative"
                                                style={{ height: "100%", display: "flex", flexDirection: "column", justifyContent: "flex-end" }}
                                            >
                                                <div
                                                    className="w-full rounded-t transition-all hover:opacity-80"
                                                    style={{
                                                        height: `${height}%`,
                                                        minHeight: point.count > 0 ? "4px" : "0",
                                                        background: point.critical > 0
                                                            ? "linear-gradient(180deg, #EF4444 0%, #7C3AED 100%)"
                                                            : "linear-gradient(180deg, #6366F1 0%, #7C3AED 100%)",
                                                    }}
                                                />
                                                <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 hidden group-hover:block z-10">
                                                    <div className="bg-popover border border-border rounded-lg p-2 shadow-lg whitespace-nowrap text-xs">
                                                        <p style={{ fontWeight: 600 }}>{new Date(point.date).toLocaleDateString()}</p>
                                                        <p>{point.count} incidents</p>
                                                        {point.critical > 0 && <p className="text-error-400">{point.critical} critical</p>}
                                                    </div>
                                                </div>
                                            </div>
                                        );
                                    })}
                                </div>
                                <div className="flex justify-between text-xs" style={{ color: "#64748B" }}>
                                    <span>{trends.length > 0 ? new Date(trends[0].date).toLocaleDateString() : ""}</span>
                                    <span>{trends.length > 0 ? new Date(trends[trends.length - 1].date).toLocaleDateString() : ""}</span>
                                </div>
                            </div>
                        )}
                    </Card>

                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                        <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                            <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                                {t("reports.severityDistribution")}
                            </h3>
                            {!severity || severity.length === 0 ? (
                                <p style={{ fontSize: "0.875rem", color: "#94A3B8", textAlign: "center", padding: "2rem 0" }}>
                                    {t("reports.noData")}
                                </p>
                            ) : (
                                <div className="space-y-3">
                                    {severity.map((s) => (
                                        <div key={s.severity}>
                                            <div className="flex items-center justify-between mb-1">
                                                <div className="flex items-center gap-2">
                                                    <div
                                                        className="w-3 h-3 rounded-full"
                                                        style={{ backgroundColor: SEVERITY_COLORS[s.severity] ?? "#94A3B8" }}
                                                    />
                                                    <span style={{ fontSize: "0.875rem", fontWeight: 500 }}>{s.severity}</span>
                                                </div>
                                                <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                                                    {s.count} ({s.percentage}%)
                                                </span>
                                            </div>
                                            <div className="w-full h-2 rounded-full bg-surface-light/20 overflow-hidden">
                                                <div
                                                    className="h-full rounded-full transition-all"
                                                    style={{
                                                        width: `${s.percentage}%`,
                                                        backgroundColor: SEVERITY_COLORS[s.severity] ?? "#94A3B8",
                                                    }}
                                                />
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </Card>

                        <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                            <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                                <Clock className="w-5 h-5 inline-block mr-2 text-warning-400" />
                                {t("reports.responseTimeTrends")}
                            </h3>
                            {!mtt || mtt.length === 0 ? (
                                <p style={{ fontSize: "0.875rem", color: "#94A3B8", textAlign: "center", padding: "2rem 0" }}>
                                    {t("reports.noData")}
                                </p>
                            ) : (
                                <div className="space-y-3">
                                    {mtt.map((point, i) => (
                                        <div key={i} className="flex items-center gap-4 p-2 rounded-lg hover:bg-surface-light/10">
                                            <span style={{ fontSize: "0.75rem", color: "#64748B", minWidth: "80px" }}>
                                                {new Date(point.date).toLocaleDateString()}
                                            </span>
                                            <div className="flex-1 flex items-center gap-4">
                                                <div className="flex items-center gap-1">
                                                    <Badge className="bg-warning-500/10 text-warning-400 border-warning-500/20 border text-xs">
                                                        MTTA: {Math.round(point.mttaMinutes)}m
                                                    </Badge>
                                                </div>
                                                <div className="flex items-center gap-1">
                                                    <Badge className="bg-success-500/10 text-success-400 border-success-500/20 border text-xs">
                                                        MTTR: {Math.round(point.mttrMinutes)}m
                                                    </Badge>
                                                </div>
                                                <span style={{ fontSize: "0.75rem", color: "#64748B" }}>
                                                    ({point.incidentCount} incidents)
                                                </span>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </Card>
                    </div>

                    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                        <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                            <Shield className="w-5 h-5 inline-block mr-2 text-info-400" />
                            {t("reports.serviceUptime")}
                        </h3>
                        {!uptime || uptime.length === 0 ? (
                            <p style={{ fontSize: "0.875rem", color: "#94A3B8", textAlign: "center", padding: "2rem 0" }}>
                                {t("reports.noServicesFound")}
                            </p>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="w-full">
                                    <thead>
                                        <tr className="border-b border-border">
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colService")}</th>
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colUptime")}</th>
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colIncidents")}</th>
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colDowntime")}</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {uptime.map((s) => (
                                            <tr key={s.serviceId} className="border-b border-border">
                                                <td style={{ padding: "0.75rem 0", fontSize: "0.875rem", fontWeight: 600 }}>
                                                    {s.serviceName}
                                                </td>
                                                <td style={{ padding: "0.75rem 0" }}>
                                                    <div className="flex items-center gap-2">
                                                        <div className="w-24 h-2 rounded-full bg-surface-light/20 overflow-hidden">
                                                            <div
                                                                className="h-full rounded-full"
                                                                style={{
                                                                    width: `${s.uptimePercent}%`,
                                                                    backgroundColor: s.uptimePercent >= 99.9 ? "#22C55E"
                                                                        : s.uptimePercent >= 99 ? "#EAB308"
                                                                            : "#EF4444",
                                                                }}
                                                            />
                                                        </div>
                                                        <span style={{
                                                            fontSize: "0.875rem",
                                                            fontWeight: 600,
                                                            color: s.uptimePercent >= 99.9 ? "#22C55E"
                                                                : s.uptimePercent >= 99 ? "#EAB308"
                                                                    : "#EF4444",
                                                        }}>
                                                            {s.uptimePercent}%
                                                        </span>
                                                    </div>
                                                </td>
                                                <td style={{ padding: "0.75rem 0", fontSize: "0.875rem", color: "#94A3B8" }}>
                                                    {s.incidentCount}
                                                </td>
                                                <td style={{ padding: "0.75rem 0", fontSize: "0.875rem", color: "#94A3B8" }}>
                                                    {s.totalDowntimeMinutes < 60
                                                        ? `${Math.round(s.totalDowntimeMinutes)}m`
                                                        : `${(s.totalDowntimeMinutes / 60).toFixed(1)}h`}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </Card>

                    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                        <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                            <Users className="w-5 h-5 inline-block mr-2 text-brand-400" />
                            {t("reports.teamPerformance")}
                        </h3>
                        {!teamPerf || teamPerf.length === 0 ? (
                            <p style={{ fontSize: "0.875rem", color: "#94A3B8", textAlign: "center", padding: "2rem 0" }}>
                                {t("reports.noTeamData")}
                            </p>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="w-full">
                                    <thead>
                                        <tr className="border-b border-border">
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colTeam")}</th>
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colIncidents")}</th>
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colResolved")}</th>
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colAvgMtta")}</th>
                                            <th style={{ fontSize: "0.75rem", fontWeight: 600, textAlign: "left", padding: "0.75rem 0" }}>{t("reports.colAvgMttr")}</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {teamPerf.map((t) => (
                                            <tr key={t.teamId} className="border-b border-border">
                                                <td style={{ padding: "0.75rem 0", fontSize: "0.875rem", fontWeight: 600 }}>
                                                    {t.teamName}
                                                </td>
                                                <td style={{ padding: "0.75rem 0", fontSize: "0.875rem", color: "#94A3B8" }}>
                                                    {t.totalIncidents}
                                                </td>
                                                <td style={{ padding: "0.75rem 0" }}>
                                                    <Badge className="bg-success-500/10 text-success-400 border-success-500/20 border text-xs">
                                                        {t.resolvedCount}
                                                    </Badge>
                                                </td>
                                                <td style={{ padding: "0.75rem 0", fontSize: "0.875rem" }}>
                                                    <span style={{ color: t.avgAcknowledgeMinutes <= 5 ? "#22C55E" : t.avgAcknowledgeMinutes <= 15 ? "#EAB308" : "#EF4444" }}>
                                                        {Math.round(t.avgAcknowledgeMinutes)}m
                                                    </span>
                                                </td>
                                                <td style={{ padding: "0.75rem 0", fontSize: "0.875rem" }}>
                                                    <span style={{ color: t.avgResolveMinutes <= 30 ? "#22C55E" : t.avgResolveMinutes <= 120 ? "#EAB308" : "#EF4444" }}>
                                                        {t.avgResolveMinutes < 60
                                                            ? `${Math.round(t.avgResolveMinutes)}m`
                                                            : `${(t.avgResolveMinutes / 60).toFixed(1)}h`}
                                                    </span>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </Card>
                </>
            )}
        </div>
    );
}
