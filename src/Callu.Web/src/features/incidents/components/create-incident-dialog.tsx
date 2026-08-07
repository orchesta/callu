import { useEffect, useId, useState } from "react";
import { useNavigate } from "react-router";
import { Plus } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useServices } from "@/features/services/hooks/use-services";
import { useTeams } from "@/features/teams/hooks/use-teams";
import { useCreateIncident } from "../hooks/use-incidents";
import { t } from "@/shared/locales/i18n";

const SEVERITIES = ["Critical", "High", "Medium", "Low"] as const;

const NONE = "__none__";

const TITLE_MAX = 200;
const DESCRIPTION_MAX = 2000;

interface CreateIncidentDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CreateIncidentDialog({ open, onOpenChange }: CreateIncidentDialogProps) {
  const formId = useId();
  const navigate = useNavigate();
  const createIncident = useCreateIncident();

  const { data: services } = useServices();
  const { data: teams } = useTeams();

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [severity, setSeverity] = useState<string>("High");
  const [serviceId, setServiceId] = useState(NONE);
  const [teamId, setTeamId] = useState(NONE);

  useEffect(() => {
    if (!open) return;
    setTitle("");
    setDescription("");
    setSeverity("High");
    setServiceId(NONE);
    setTeamId(NONE);
  }, [open]);

  const trimmedTitle = title.trim();
  const canSubmit = trimmedTitle.length > 0 && !createIncident.isPending;

  const handleCreate = async () => {
    if (!canSubmit) return;

    const result = await createIncident.mutateAsync({
      title: trimmedTitle,
      description: description.trim() || undefined,
      severity,
      serviceId: serviceId === NONE ? undefined : serviceId,
      teamId: teamId === NONE ? undefined : teamId,
    });

    onOpenChange(false);

    // Only a written incident has somewhere to navigate to; a suppressed one was never stored,
    // and the hook has already said so.
    if (result?.outcome === "Created" && result.incident) {
      navigate(`/incidents/${result.incident.id}`);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="bg-card border-border sm:max-w-[600px]">
        <DialogHeader>
          <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
            {t("incidents.createIncident")}
          </DialogTitle>
          <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {t("incidents.triggerDesc")}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-5 py-4">
          <div className="space-y-2">
            <label
              htmlFor={`${formId}-title`}
              style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}
            >
              {t("incidents.incidentTitle")} <span className="text-error-500">*</span>
            </label>
            <Input
              id={`${formId}-title`}
              placeholder={t("incidents.titlePlaceholder")}
              value={title}
              maxLength={TITLE_MAX}
              onChange={(e) => setTitle(e.target.value)}
              className="bg-input-background"
            />
          </div>

          <div className="space-y-2">
            <label
              htmlFor={`${formId}-description`}
              style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}
            >
              {t("common.description")}
            </label>
            <Textarea
              id={`${formId}-description`}
              placeholder={t("incidents.descriptionPlaceholder")}
              value={description}
              maxLength={DESCRIPTION_MAX}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              className="bg-input-background resize-none"
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <label
                htmlFor={`${formId}-severity`}
                style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}
              >
                {t("incidents.severity")} <span className="text-error-500">*</span>
              </label>
              <Select value={severity} onValueChange={setSeverity}>
                <SelectTrigger id={`${formId}-severity`} className="bg-input-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SEVERITIES.map((s) => (
                    <SelectItem key={s} value={s}>
                      {t(`incidents.${s.toLowerCase()}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-2">
              <label
                htmlFor={`${formId}-service`}
                style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}
              >
                {t("incidents.service")}
              </label>
              <Select value={serviceId} onValueChange={setServiceId}>
                <SelectTrigger id={`${formId}-service`} className="bg-input-background">
                  <SelectValue placeholder={t("incidents.selectService")} />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("incidents.noService")}</SelectItem>
                  {services.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="space-y-2">
            <label
              htmlFor={`${formId}-team`}
              style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}
            >
              {t("incidents.team")}
            </label>
            <Select value={teamId} onValueChange={setTeamId}>
              <SelectTrigger id={`${formId}-team`} className="bg-input-background">
                <SelectValue placeholder={t("incidents.noTeam")} />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE}>{t("incidents.noTeam")}</SelectItem>
                {(teams ?? []).map((team) => (
                  <SelectItem key={team.id} value={team.id}>
                    {team.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">{t("incidents.createPagingHint")}</p>
          </div>
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={createIncident.isPending}
            className="bg-input-background"
          >
            {t("common.cancel")}
          </Button>
          <Button
            onClick={handleCreate}
            disabled={!canSubmit}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {createIncident.isPending ? (
              <>
                <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                {t("common.creating")}
              </>
            ) : (
              <>
                <Plus className="w-4 h-4 mr-2" />
                {t("incidents.createIncident")}
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
