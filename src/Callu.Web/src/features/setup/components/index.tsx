import { useState } from "react";
import { t } from "@/shared/locales/i18n";
import { Card } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Badge } from "@/shared/components/ui/badge";
import {
    Check,
    ChevronRight,
    ChevronLeft,
    Users,
    Bell,
    Shield,
    Rocket,
    Loader2,
    SkipForward,
} from "lucide-react";
import { useCreateTeam } from "@/features/teams/hooks/use-teams";

interface StepConfig {
    id: string;
    title: string;
    description: string;
    icon: React.ReactNode;
}

const STEPS: StepConfig[] = [
    {
        id: "team",
        title: "setup.stepTeamTitle",
        description: "setup.stepTeamDesc",
        icon: <Users className="w-5 h-5" />,
    },
    {
        id: "notifications",
        title: "setup.stepNotificationsTitle",
        description: "setup.stepNotificationsDesc",
        icon: <Bell className="w-5 h-5" />,
    },
    {
        id: "integrations",
        title: "setup.stepIntegrationsTitle",
        description: "setup.stepIntegrationsDesc",
        icon: <Shield className="w-5 h-5" />,
    },
    {
        id: "ready",
        title: "setup.stepReadyTitle",
        description: "setup.stepReadyDesc",
        icon: <Rocket className="w-5 h-5" />,
    },
];

export function SetupWizard() {
    const [currentStep, setCurrentStep] = useState(0);
    const [completed, setCompleted] = useState<Set<string>>(new Set());
    const [teamName, setTeamName] = useState("");
    const [isSaving, setIsSaving] = useState(false);
    const [stepError, setStepError] = useState("");

    const createTeam = useCreateTeam();

    const step = STEPS[currentStep];

    const skipStep = () => {
        if (currentStep < STEPS.length - 1) {
            setStepError("");
            setCurrentStep(currentStep + 1);
        }
    };

    const skipEntireWizard = () => {
        window.location.href = "/";
    };

    const handleContinue = async () => {
        setStepError("");
        setIsSaving(true);

        try {
            if (step.id === "team" && teamName.trim()) {
                await createTeam.mutateAsync({
                    name: teamName.trim(),
                });
            }

            setCompleted((prev) => new Set([...prev, step.id]));
            if (currentStep < STEPS.length - 1) {
                setCurrentStep(currentStep + 1);
            }
        } catch (err: unknown) {
            setStepError(err instanceof Error ? err.message : t("setup.somethingWentWrong"));
        } finally {
            setIsSaving(false);
        }
    };

    const handleFinish = () => {
        window.location.href = "/";
    };

    const canContinue = (() => {
        if (step.id === "team") return teamName.trim().length > 0;
        return true;
    })();

    return (
        <div className="min-h-screen bg-background flex items-center justify-center p-6">
            <div className="w-full max-w-3xl">
                <div className="text-center mb-8">
                    <h1
                        style={{
                            fontSize: "1.875rem",
                            fontWeight: 700,
                            background: "linear-gradient(to right, #6366f1, #a78bfa)",
                            WebkitBackgroundClip: "text",
                            WebkitTextFillColor: "transparent",
                        }}
                    >
                        {t("setup.welcomeToCalluApp")}
                    </h1>
                    <p style={{ color: "#94A3B8", marginTop: "0.5rem" }}>
                        {t("setup.setupSubtitle")}
                    </p>
                    <button
                        onClick={skipEntireWizard}
                        style={{
                            marginTop: "0.75rem",
                            fontSize: "0.8rem",
                            color: "#64748B",
                            background: "none",
                            border: "none",
                            cursor: "pointer",
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.25rem",
                        }}
                        onMouseEnter={(e) => (e.currentTarget.style.color = "#94A3B8")}
                        onMouseLeave={(e) => (e.currentTarget.style.color = "#64748B")}
                    >
                        <SkipForward className="w-3.5 h-3.5" />
                        {t("setup.skipSetup")}
                    </button>
                </div>

                <div className="flex items-center justify-center gap-2 mb-8">
                    {STEPS.map((s, i) => (
                        <div key={s.id} className="flex items-center gap-2">
                            <div
                                className={`flex items-center justify-center w-8 h-8 rounded-full text-xs font-bold transition-all ${completed.has(s.id)
                                    ? "bg-green-500 text-white"
                                    : i === currentStep
                                        ? "bg-brand-500 text-white shadow-lg shadow-brand-500/30"
                                        : "bg-card border border-border text-muted-foreground"
                                    }`}
                            >
                                {completed.has(s.id) ? <Check className="w-4 h-4" /> : i + 1}
                            </div>
                            {i < STEPS.length - 1 && (
                                <div
                                    className={`h-0.5 w-8 transition-colors ${completed.has(s.id) ? "bg-green-500" : "bg-border"
                                        }`}
                                />
                            )}
                        </div>
                    ))}
                </div>

                <Card className="p-8 bg-card/80 backdrop-blur-sm border-border">
                    <div className="flex items-center gap-3 mb-6">
                        <div className="p-2 rounded-lg bg-brand-500/10 text-brand-400">
                            {step.icon}
                        </div>
                        <div>
                            <h2 style={{ fontSize: "1.25rem", fontWeight: 600 }}>
                                {t(step.title)}
                            </h2>
                            <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                                {t(step.description)}
                            </p>
                        </div>
                    </div>

                    {step.id === "team" && (
                        <div className="space-y-4">
                            <div>
                                <label
                                    style={{
                                        fontSize: "0.75rem",
                                        fontWeight: 600,
                                        color: "#94A3B8",
                                    }}
                                >
                                    {t("setup.teamName")}
                                </label>
                                <Input
                                    className="mt-1"
                                    placeholder={t("setup.teamNamePlaceholder")}
                                    value={teamName}
                                    onChange={(e) => setTeamName(e.target.value)}
                                />
                            </div>
                            <div className="grid grid-cols-3 gap-3">
                                {["Engineering", "DevOps", "SRE"].map((name) => (
                                    <Button
                                        key={name}
                                        variant="outline"
                                        size="sm"
                                        className="text-sm"
                                        onClick={() => setTeamName(name)}
                                    >
                                        {name}
                                    </Button>
                                ))}
                            </div>
                        </div>
                    )}

                    {step.id === "notifications" && (
                        <div className="space-y-3">
                            {[
                                {
                                    name: "Slack",
                                    desc: t("setup.slackDesc"),
                                },
                                {
                                    name: "Microsoft Teams",
                                    desc: t("setup.teamsDesc"),
                                },
                                {
                                    name: "Email",
                                    desc: t("setup.emailDesc"),
                                },
                            ].map((ch) => (
                                <div
                                    key={ch.name}
                                    className="flex items-center justify-between p-3 rounded-lg bg-card border border-border"
                                >
                                    <div>
                                        <span style={{ fontWeight: 600, fontSize: "0.875rem" }}>
                                            {ch.name}
                                        </span>
                                        <p
                                            style={{
                                                fontSize: "0.75rem",
                                                color: "#64748B",
                                            }}
                                        >
                                            {ch.desc}
                                        </p>
                                    </div>
                                    <Badge className="bg-muted text-muted-foreground text-xs">
                                        {t("setup.configureLater")}
                                    </Badge>
                                </div>
                            ))}
                            <p
                                style={{
                                    fontSize: "0.875rem",
                                    color: "#94A3B8",
                                    marginTop: "0.5rem",
                                }}
                            >
                                <strong style={{ color: "#e2e8f0" }}>
                                    {t("setup.settingsNotifications")}
                                </strong>{" "}
                                {t("setup.canConfigureNotifications")}
                            </p>
                        </div>
                    )}

                    {step.id === "integrations" && (
                        <div className="space-y-3">
                            {[
                                {
                                    name: t("setup.monitoringTools"),
                                    desc: "Datadog, PagerDuty, Grafana",
                                },
                                {
                                    name: t("setup.webhooks"),
                                    desc: t("setup.webhooksDesc"),
                                },
                                {
                                    name: t("setup.apiKeys"),
                                    desc: t("setup.apiKeysDesc"),
                                },
                            ].map((item) => (
                                <div
                                    key={item.name}
                                    className="flex items-center justify-between p-3 rounded-lg bg-card border border-border"
                                >
                                    <div>
                                        <span style={{ fontWeight: 600, fontSize: "0.875rem" }}>
                                            {item.name}
                                        </span>
                                        <p
                                            style={{
                                                fontSize: "0.75rem",
                                                color: "#64748B",
                                            }}
                                        >
                                            {item.desc}
                                        </p>
                                    </div>
                                    <Badge className="bg-muted text-muted-foreground text-xs">
                                        {t("common.optional")}
                                    </Badge>
                                </div>
                            ))}
                        </div>
                    )}

                    {step.id === "ready" && (
                        <div className="text-center py-6">
                            <h3
                                style={{
                                    fontSize: "1.25rem",
                                    fontWeight: 600,
                                    marginBottom: "0.5rem",
                                }}
                            >
                                {t("setup.allSet")}
                            </h3>
                            <p style={{ color: "#94A3B8", maxWidth: "28rem", margin: "0 auto" }}>
                                {t("setup.platformReadyMessage")}
                            </p>
                        </div>
                    )}

                    {stepError && (
                        <div className="mt-4 p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm">
                            {stepError}
                        </div>
                    )}

                    <div className="flex justify-between mt-8 pt-6 border-t border-border">
                        <Button
                            variant="outline"
                            disabled={currentStep === 0}
                            onClick={() => {
                                setStepError("");
                                setCurrentStep(currentStep - 1);
                            }}
                        >
                            <ChevronLeft className="w-4 h-4 mr-1" /> {t("common.back")}
                        </Button>
                        {step.id === "ready" ? (
                            <Button
                                onClick={handleFinish}
                                style={{
                                    background: "linear-gradient(to right, #6366f1, #a78bfa)",
                                }}
                            >
                                {t("setup.goToDashboard")}
                                <Rocket className="w-4 h-4 ml-2" />
                            </Button>
                        ) : (
                            <div className="flex gap-2">
                                <Button
                                    variant="ghost"
                                    onClick={skipStep}
                                    disabled={isSaving}
                                >
                                    {t("common.skip")}
                                </Button>
                                <Button
                                    onClick={handleContinue}
                                    disabled={!canContinue || isSaving}
                                >
                                    {isSaving && (
                                        <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                                    )}
                                    {isSaving ? t("common.saving") : t("common.continue")}
                                    {!isSaving && <ChevronRight className="w-4 h-4 ml-1" />}
                                </Button>
                            </div>
                        )}
                    </div>
                </Card>
            </div>
        </div>
    );
}
