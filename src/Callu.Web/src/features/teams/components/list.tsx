import { useState, useMemo, useCallback } from "react";
import React from "react";
import { t } from "@/shared/locales/i18n";
import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Card } from "@/shared/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Plus,
  Users,
  Edit,
  Trash2,
  AlertCircle,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { StatCard } from "@/shared/components/stat-card";
import { PageHeader } from "@/shared/components/page-header";
import { SearchInput } from "@/shared/components/search-input";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";

import type { TeamDto, CreateTeamRequest } from "../types/team.types";
import { useTeams, useCreateTeam, useDeleteTeam } from "../hooks/use-teams";

const teamColors = [
  { value: "#3E7BFA", label: "Blue" },
  { value: "#22C55E", label: "Green" },
  { value: "#FB923C", label: "Orange" },
  { value: "#A855F7", label: "Purple" },
  { value: "#EC4899", label: "Pink" },
  { value: "#EF4444", label: "Red" },
  { value: "#F59E0B", label: "Amber" },
  { value: "#10B981", label: "Emerald" },
];

export function TeamsList() {
  const [searchQuery, setSearchQuery] = useState("");
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [selectedTeam, setSelectedTeam] = useState<TeamDto | null>(null);
  const [formData, setFormData] = useState<CreateTeamRequest>({
    name: "",
    description: "",
    color: teamColors[0].value,
  });

  const { data: teams = [], isLoading, isError } = useTeams();
  const createTeam = useCreateTeam();
  const deleteTeam = useDeleteTeam();

  const filteredTeams = useMemo(() => {
    if (!searchQuery) return teams;
    const q = searchQuery.toLowerCase();
    return teams.filter(
      (team) =>
        team.name.toLowerCase().includes(q) ||
        team.description?.toLowerCase().includes(q)
    );
  }, [teams, searchQuery]);

  const handleCreateTeam = () => {
    setFormData({ name: "", description: "", color: teamColors[0].value });
    setIsCreateModalOpen(true);
  };

  const handleSubmitTeam = async () => {
    await createTeam.mutateAsync(formData);
    setIsCreateModalOpen(false);
  };

  const handleDeleteTeam = useCallback((team: TeamDto) => {
    setSelectedTeam(team);
    setIsDeleteModalOpen(true);
  }, []);

  const handleConfirmDelete = async () => {
    if (!selectedTeam) return;
    await deleteTeam.mutateAsync(selectedTeam.id);
    setIsDeleteModalOpen(false);
    setSelectedTeam(null);
  };

  const isFormValid = formData.name.trim().length > 0;
  const isSubmitting = createTeam.isPending || deleteTeam.isPending;

  const stats = useMemo(
    () => ({
      totalTeams: filteredTeams.length,
      totalMembers: filteredTeams.reduce((sum, t) => sum + t.memberCount, 0),
      avgSize:
        filteredTeams.length > 0
          ? (
            filteredTeams.reduce((sum, t) => sum + t.memberCount, 0) /
            filteredTeams.length
          ).toFixed(1)
          : "0",
    }),
    [filteredTeams]
  );

  return (
    <>
      <div className="p-6 space-y-6">
        <PageHeader
          title={t("teams.title")}
          subtitle={t("teams.subtitle")}
          action={
            <Button
              onClick={handleCreateTeam}
              className="bg-brand-500 hover:bg-brand-600 text-white shadow-lg shadow-brand-500/20"
            >
              <Plus className="w-4 h-4 mr-2" />
              {t("teams.createTeam")}
            </Button>
          }
        />

        <SearchInput
          placeholder={t("teams.searchTeams")}
          value={searchQuery}
          onChange={setSearchQuery}
        />

        <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
          <StatCard label={t("teams.totalTeams")} value={stats.totalTeams} />
          <StatCard label={t("teams.totalMembers")} value={stats.totalMembers} color="#3E7BFA" borderColor="border-brand-500/20" />
          <StatCard label={t("teams.avgTeamSize")} value={stats.avgSize} color="#22C55E" borderColor="border-success-500/20" />
        </div>

        {isLoading ? (
          <LoadingState message={t("teams.loading")} />
        ) : isError ? (
          <ErrorState title={t("teams.loadFailed")} message={t("teams.loadFailedDesc")} />
        ) : filteredTeams.length > 0 ? (
          <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-6">
            {filteredTeams.map((team) => (
              <TeamCard
                key={team.id}
                team={team}
                onDelete={() => handleDeleteTeam(team)}
              />
            ))}
          </div>
        ) : (
          <EmptyState
            icon={Users}
            title={t("teams.noTeamsFound")}
            description={t("teams.noTeamsFoundDesc")}
            action={
              <Button
                onClick={handleCreateTeam}
                className="bg-brand-500 hover:bg-brand-600"
              >
                <Plus className="w-4 h-4 mr-2" />
                {t("teams.createTeam")}
              </Button>
            }
          />
        )}
      </div>

      <Dialog open={isCreateModalOpen} onOpenChange={setIsCreateModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[600px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {t("teams.createTeamTitle")}
            </DialogTitle>
            <DialogDescription
              style={{ fontSize: "0.875rem", color: "#94A3B8" }}
            >
              {t("teams.createTeamDesc")}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-5 py-4">
            <div>
              <label
                style={{
                  fontSize: "0.875rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                  display: "block",
                }}
              >
                {t("teams.teamNameLabel")} <span className="text-error-500">*</span>
              </label>
              <Input
                placeholder={t("teams.teamNamePlaceholder2")}
                value={formData.name}
                onChange={(e) =>
                  setFormData({ ...formData, name: e.target.value })
                }
                className="bg-input-background"
              />
            </div>

            <div>
              <label
                style={{
                  fontSize: "0.875rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                  display: "block",
                }}
              >
                {t("teams.descriptionLabel")}
              </label>
              <Textarea
                placeholder={t("teams.descriptionPlaceholder")}
                value={formData.description ?? ""}
                onChange={(e) =>
                  setFormData({ ...formData, description: e.target.value })
                }
                rows={3}
                className="bg-input-background resize-none"
              />
            </div>

            <div>
              <label
                style={{
                  fontSize: "0.875rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                  display: "block",
                }}
              >
                {t("teams.teamColor")}
              </label>
              <div className="grid grid-cols-8 gap-3">
                {teamColors.map((color) => (
                  <button
                    key={color.value}
                    onClick={() =>
                      setFormData({ ...formData, color: color.value })
                    }
                    className={`w-full aspect-square rounded-lg transition-all ${formData.color === color.value
                      ? "ring-2 ring-offset-2 ring-offset-card scale-110"
                      : "hover:scale-105"
                      }`}
                    style={
                      {
                        backgroundColor: color.value,
                        "--tw-ring-color": color.value,
                      } as React.CSSProperties
                    }
                    title={color.label}
                  />
                ))}
              </div>
            </div>

            <div className="p-4 rounded-lg bg-brand-500/5 border border-brand-500/20 flex gap-3">
              <AlertCircle className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
              <div>
                <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                  {t("teams.createNote")}
                </p>
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsCreateModalOpen(false)}
              disabled={isSubmitting}
              className="bg-input-background"
            >
              {t("common.cancel")}
            </Button>
            <Button
              onClick={handleSubmitTeam}
              disabled={!isFormValid || isSubmitting}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {createTeam.isPending ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                  {t("common.creating")}
                </>
              ) : (
                <>
                  <Plus className="w-4 h-4 mr-2" />
                  {t("teams.createTeam")}
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={isDeleteModalOpen}
        onOpenChange={setIsDeleteModalOpen}
        title={t("teams.deleteTeamTitle")}
        message={t("teams.deleteTeamMsg").replace("{name}", selectedTeam?.name ?? "")}
        warning={t("teams.deleteTeamWarn")}
        onConfirm={handleConfirmDelete}
        isLoading={deleteTeam.isPending}
        confirmLabel={t("teams.deleteTeam")}
        cancelLabel={t("common.cancel")}
      />
    </>
  );
}

interface TeamCardProps {
  team: TeamDto;
  onDelete: () => void;
}

const TeamCard = React.memo(function TeamCard({ team, onDelete }: TeamCardProps) {
  const color = team.color ?? "#3E7BFA";

  return (
    <Card className="overflow-hidden bg-card/80 backdrop-blur-sm border-border hover:border-border-light transition-all hover:shadow-lg">
      <div
        className="h-2"
        style={{
          background: `linear-gradient(90deg, ${color} 0%, ${color}80 100%)`,
        }}
      />

      <div className="p-6">
        <div className="flex items-start justify-between gap-3 mb-4">
          <div className="flex items-start gap-3 flex-1 min-w-0">
            <div
              className="w-10 h-10 rounded-lg flex items-center justify-center flex-shrink-0"
              style={{ backgroundColor: `${color}20` }}
            >
              <Users className="w-5 h-5" style={{ color }} />
            </div>
            <div className="flex-1 min-w-0">
              <Link
                to={`/teams/${team.id}`}
                className="font-semibold hover:text-brand-500 transition-colors block truncate"
                style={{ fontSize: "1.0625rem" }}
              >
                {team.name}
              </Link>
              {team.description && (
                <p
                  style={{ fontSize: "0.8125rem", color: "#94A3B8" }}
                  className="line-clamp-2 mt-1"
                >
                  {team.description}
                </p>
              )}
            </div>
          </div>
        </div>

        <div className="mb-4 grid grid-cols-1 gap-3">
          <div className="p-3 rounded-lg bg-surface-light/20 border border-border">
            <div className="flex items-center gap-2 mb-1">
              <Users className="w-3.5 h-3.5 text-muted-foreground" />
              <p
                style={{
                  fontSize: "0.75rem",
                  color: "#94A3B8",
                  fontWeight: 600,
                }}
              >
                {t("teams.membersLabel")}
              </p>
            </div>
            <p style={{ fontSize: "1.25rem", fontWeight: 700 }}>
              {team.memberCount}
            </p>
          </div>
        </div>

        <p
          style={{ fontSize: "0.75rem", color: "#64748B", marginBottom: "1rem" }}
        >
          {t("teams.createdDate")}{" "}
          {new Date(team.createdAt).toLocaleDateString("en-US", {
            month: "short",
            day: "numeric",
            year: "numeric",
          })}
        </p>

        <div className="flex gap-2 pt-4 border-t border-border">
          <Link to={`/teams/${team.id}`} className="flex-1">
            <Button variant="outline" className="w-full bg-input-background">
              <Edit className="w-4 h-4 mr-2" />
              {t("teams.manageTeam")}
            </Button>
          </Link>
          <Button
            variant="outline"
            size="sm"
            onClick={onDelete}
            className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
          >
            <Trash2 className="w-4 h-4" />
          </Button>
        </div>
      </div>
    </Card>
  );
});