import { useState, useEffect, useMemo } from "react";
import { onLocaleChange, t } from "@/shared/locales/i18n";
import { useParams, useNavigate, Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { Card } from "@/shared/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import {
  ChevronRight,
  Home,
  Save,
  Trash2,
  Users,
  Plus,
  X,
  Mail,
  Phone,
  Crown,
  User,
  Eye,
  AlertCircle,
  Info,
  Loader2,
} from "lucide-react";

import type { TeamMemberDto } from "../types/team.types";
import {
  useTeam,
  useCreateTeam,
  useUpdateTeam,
  useDeleteTeam,
  useAddMember,
  useRemoveMember,
  useUpdateMemberRole,
} from "../hooks/use-teams";
import { useUsers } from "@/features/users/hooks/use-users";

const EMPTY_TEAM_MEMBERS: TeamMemberDto[] = [];

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

const memberColors = [
  "#3E7BFA", "#22C55E", "#FB923C", "#A855F7", "#EC4899",
  "#EF4444", "#F59E0B", "#10B981", "#8B5CF6", "#06B6D4",
];

function getMemberColor(name: string): string {
  let hash = 0;
  for (let i = 0; i < name.length; i++) {
    hash = name.charCodeAt(i) + ((hash << 5) - hash);
  }
  return memberColors[Math.abs(hash) % memberColors.length];
}

function getInitials(name: string): string {
  return name
    .split(" ")
    .map((n) => n[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);
}

export function TeamDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const isNew = id === "new";

  const { data: teamDetail, isLoading, isError } = useTeam(isNew ? "" : id!);
  const createTeam = useCreateTeam();
  const updateTeam = useUpdateTeam();
  const deleteTeamMutation = useDeleteTeam();
  const addMember = useAddMember();
  const removeMember = useRemoveMember();
  const updateMemberRole = useUpdateMemberRole();
  const { data: allUsers = [] } = useUsers();

  const [teamName, setTeamName] = useState("");
  const [description, setDescription] = useState("");
  const [teamColor, setTeamColor] = useState("#3E7BFA");

  useEffect(() => {
    if (teamDetail) {
      setTeamName(teamDetail.name);
      setDescription(teamDetail.description ?? "");
      setTeamColor(teamDetail.color ?? "#3E7BFA");
    }
  }, [teamDetail]);

  const members: TeamMemberDto[] = teamDetail?.members ?? EMPTY_TEAM_MEMBERS;

  const availableUsers = useMemo(
    () => allUsers.filter((u) => !members.find((m) => m.userId === u.id)),
    [allUsers, members]
  );

  const [i18nLocaleTick, setI18nLocaleTick] = useState(0);
  useEffect(() => onLocaleChange(() => setI18nLocaleTick((n) => n + 1)), []);

  const roleOptions = useMemo(
    () => [
      {
        value: "lead" as const,
        label: t("teams.detail.teamLead"),
        icon: Crown,
        description: t("teams.detail.roleSelectLeadDesc"),
      },
      {
        value: "member" as const,
        label: t("teams.detail.roleMember"),
        icon: User,
        description: t("teams.detail.roleSelectMemberDesc"),
      },
      {
        value: "observer" as const,
        label: t("teams.detail.roleObserver"),
        icon: Eye,
        description: t("teams.detail.roleSelectObserverDesc"),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [i18nLocaleTick],
  );

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isAddMemberOpen, setIsAddMemberOpen] = useState(false);
  const [isRemoveMemberModalOpen, setIsRemoveMemberModalOpen] = useState(false);
  const [selectedMember, setSelectedMember] = useState<TeamMemberDto | null>(null);
  const [newMemberId, setNewMemberId] = useState("");
  const [newMemberRole, setNewMemberRole] = useState<string>("member");

  const getRoleBadgeClass = (role: string) => {
    switch (role.toLowerCase()) {
      case "lead":
        return "bg-warning-500/10 text-warning-500 border-warning-500/20";
      case "member":
        return "bg-brand-500/10 text-brand-500 border-brand-500/20";
      case "observer":
        return "bg-muted/20 text-muted-foreground border-muted/20";
      default:
        return "bg-muted/20 text-muted-foreground border-muted/20";
    }
  };

  const handleAddMember = () => {
    setNewMemberId("");
    setNewMemberRole("member");
    setIsAddMemberOpen(true);
  };

  const handleSubmitAddMember = async () => {
    if (!id || !newMemberId) return;
    await addMember.mutateAsync({
      teamId: id,
      userId: newMemberId,
      role: newMemberRole,
    });
    setIsAddMemberOpen(false);
  };

  const handleRemoveMember = (member: TeamMemberDto) => {
    setSelectedMember(member);
    setIsRemoveMemberModalOpen(true);
  };

  const handleConfirmRemoveMember = async () => {
    if (!id || !selectedMember) return;
    await removeMember.mutateAsync({
      teamId: id,
      memberId: selectedMember.id,
    });
    setIsRemoveMemberModalOpen(false);
    setSelectedMember(null);
  };

  const handleChangeRole = async (memberId: string, newRole: string) => {
    if (!id) return;
    await updateMemberRole.mutateAsync({
      teamId: id,
      memberId,
      role: newRole,
    });
  };

  const handleSave = async () => {
    if (isNew) {
      await createTeam.mutateAsync({
        name: teamName,
        description: description || undefined,
        color: teamColor,
      });
    } else {
      await updateTeam.mutateAsync({
        id: id!,
        name: teamName,
        description: description || undefined,
        color: teamColor,
      });
    }
    navigate("/teams");
  };

  const handleDelete = async () => {
    if (!id) return;
    await deleteTeamMutation.mutateAsync(id);
    setIsDeleteModalOpen(false);
    navigate("/teams");
  };

  const isValid = teamName.trim().length > 0;
  const hasLead = members.some((m) => m.role?.toLowerCase() === "lead");
  const isSaving = createTeam.isPending || updateTeam.isPending;

  if (!isNew && isLoading) {
    return (
      <div className="p-6 flex items-center justify-center py-24">
        <Loader2 className="w-8 h-8 animate-spin text-brand-500" />
      </div>
    );
  }

  if (!isNew && isError) {
    return (
      <div className="p-6 text-center py-24">
        <div className="w-16 h-16 rounded-full bg-error-500/10 flex items-center justify-center mx-auto mb-4">
          <AlertCircle className="w-8 h-8 text-error-500" />
        </div>
        <p style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "0.5rem" }}>
          {t("teams.detail.teamNotFound")}
        </p>
        <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginBottom: "1.5rem" }}>
          {t("teams.detail.teamNotFoundDesc")}
        </p>
        <Link to="/teams">
          <Button className="bg-brand-500 hover:bg-brand-600">{t("teams.detail.backToTeams")}</Button>
        </Link>
      </div>
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <nav className="flex items-center gap-2 text-sm">
          <Link to="/dashboard" className="text-muted-foreground hover:text-foreground transition-colors">
            <Home className="w-4 h-4" />
          </Link>
          <ChevronRight className="w-4 h-4 text-muted-foreground" />
          <Link to="/teams" className="text-muted-foreground hover:text-foreground transition-colors">
            {t("teams.title")}
          </Link>
          <ChevronRight className="w-4 h-4 text-muted-foreground" />
          <span className="text-foreground font-medium">
            {isNew ? t("teams.detail.newTeam") : teamName}
          </span>
        </nav>

        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>
              {isNew ? t("teams.detail.createTeamHeading") : t("teams.detail.manageTeamHeading")}
            </h1>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.25rem" }}>
              {t("teams.detail.configureSubtitle")}
            </p>
          </div>
          <div className="flex gap-2">
            {!isNew && (
              <Button
                variant="outline"
                onClick={() => setIsDeleteModalOpen(true)}
                className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
              >
                <Trash2 className="w-4 h-4 mr-2" />
                {t("common.delete")}
              </Button>
            )}
            <Button
              onClick={handleSave}
              disabled={!isValid || isSaving}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {isSaving ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                  {t("common.saving")}
                </>
              ) : (
                <>
                  <Save className="w-4 h-4 mr-2" />
                  {t("teams.detail.saveTeam")}
                </>
              )}
            </Button>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          <div className="lg:col-span-1 space-y-6">
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                {t("teams.detail.teamSettings")}
              </h3>
              <div className="space-y-4">
                <div>
                  <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                    {t("teams.teamNameLabel")} <span className="text-error-500">*</span>
                  </label>
                  <Input
                    placeholder={t("teams.detail.teamNamePlaceholder")}
                    value={teamName}
                    onChange={(e) => setTeamName(e.target.value)}
                    className="bg-input-background"
                  />
                </div>
                <div>
                  <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                    {t("teams.descriptionLabel")}
                  </label>
                  <Textarea
                    placeholder={t("teams.detail.descriptionPlaceholder")}
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    rows={3}
                    className="bg-input-background resize-none"
                  />
                </div>
                <div>
                  <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                    {t("teams.teamColor")}
                  </label>
                  <div className="grid grid-cols-4 gap-3">
                    {teamColors.map((color) => (
                      <button
                        key={color.value}
                        onClick={() => setTeamColor(color.value)}
                        className={`w-full aspect-square rounded-lg transition-all ${teamColor === color.value
                          ? "ring-2 ring-offset-2 ring-offset-card scale-110"
                          : "hover:scale-105"
                          }`}
                        style={{
                          backgroundColor: color.value,
                          '--tw-ring-color': color.value,
                        } as React.CSSProperties}
                        title={color.label}
                      />
                    ))}
                  </div>
                  <div className="mt-3 flex items-center gap-2">
                    <input
                      type="color"
                      value={/^#[0-9a-fA-F]{6}$/.test(teamColor) ? teamColor : "#3E7BFA"}
                      onChange={(e) => setTeamColor(e.target.value.toUpperCase())}
                      className="h-8 w-12 rounded border border-border bg-transparent cursor-pointer"
                      aria-label={t("teams.customColor")}
                    />
                    <span style={{ fontSize: "0.75rem", color: "#64748B" }}>
                      {teamColor}
                    </span>
                  </div>
                </div>
              </div>
            </Card>

            <Card className="p-6 bg-gradient-to-br from-brand-500/5 to-transparent border-brand-500/20">
              <div className="flex items-start gap-3">
                <Info className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
                <div>
                  <h4 style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                    {t("teams.detail.teamRoles")}
                  </h4>
                  <ul className="space-y-2" style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    <li className="flex gap-2">
                      <Crown className="w-3.5 h-3.5 text-warning-500 flex-shrink-0 mt-0.5" />
                      <span><strong>{t("teams.detail.roleLead")}:</strong> {t("teams.detail.leadDesc")}</span>
                    </li>
                    <li className="flex gap-2">
                      <User className="w-3.5 h-3.5 text-brand-500 flex-shrink-0 mt-0.5" />
                      <span><strong>{t("teams.detail.roleMember")}:</strong> {t("teams.detail.memberDesc")}</span>
                    </li>
                    <li className="flex gap-2">
                      <Eye className="w-3.5 h-3.5 text-muted-foreground flex-shrink-0 mt-0.5" />
                      <span><strong>{t("teams.detail.roleObserver")}:</strong> {t("teams.detail.observerDesc")}</span>
                    </li>
                  </ul>
                </div>
              </div>
            </Card>
          </div>

          <div className="lg:col-span-2 space-y-6">
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <div className="flex items-center justify-between mb-5">
                <div>
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("teams.detail.teamMembers")}</h3>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                    {members.length === 0
                      ? t("teams.detail.noMembersAssigned")
                      : t("teams.detail.membersAssigned").replace("{count}", String(members.length))}
                  </p>
                </div>
                {!isNew && (
                  <Button
                    onClick={handleAddMember}
                    disabled={availableUsers.length === 0}
                    className="bg-brand-500 hover:bg-brand-600 text-white"
                  >
                    <Plus className="w-4 h-4 mr-2" />
                    {t("teams.detail.addMember")}
                  </Button>
                )}
              </div>

              {members.length > 0 && !hasLead && (
                <div className="mb-4 p-3 rounded-lg bg-warning-500/10 border border-warning-500/20 flex items-start gap-2">
                  <AlertCircle className="w-4 h-4 text-warning-500 flex-shrink-0 mt-0.5" />
                  <p style={{ fontSize: "0.8125rem", color: "#FB923C" }}>
                    {t("teams.detail.noLeadWarning")}
                  </p>
                </div>
              )}

              {members.length > 0 ? (
                <div className="space-y-3">
                  {members.map((member) => {
                    const color = getMemberColor(member.name);
                    const initials = member.initials ?? getInitials(member.name);
                    const linkedUser = allUsers.find((u) => u.id === member.userId);

                    return (
                      <div
                        key={member.id}
                        className="flex items-center gap-4 p-4 rounded-lg bg-surface-light/20 border border-border hover:border-border-light transition-colors"
                      >
                        <div
                          className="w-12 h-12 rounded-full flex items-center justify-center text-white font-bold flex-shrink-0"
                          style={{ backgroundColor: color }}
                        >
                          {initials}
                        </div>

                        <div className="flex-1 min-w-0">
                          <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                            {member.name}
                          </p>
                          <div className="flex flex-wrap items-center gap-3 mt-1">
                            <div className="flex items-center gap-1">
                              <Mail className="w-3 h-3 text-muted-foreground" />
                              <span style={{ fontSize: "0.75rem", color: "#94A3B8" }} className="truncate">
                                {member.email}
                              </span>
                            </div>
                            {linkedUser?.phoneNumber && (
                              <div className="flex items-center gap-1">
                                <Phone className="w-3 h-3 text-muted-foreground" />
                                <span style={{ fontSize: "0.75rem", color: "#94A3B8" }} className="font-mono">
                                  {linkedUser.phoneNumber}
                                </span>
                              </div>
                            )}
                          </div>
                        </div>

                        <div className="flex items-center gap-2">
                          <Select
                            value={member.role?.toLowerCase() ?? "member"}
                            onValueChange={(value: string) => handleChangeRole(member.id, value)}
                          >
                            <SelectTrigger className={`w-[140px] ${getRoleBadgeClass(member.role)} border`}>
                              <div className="flex items-center gap-2">
                                <SelectValue />
                              </div>
                            </SelectTrigger>
                            <SelectContent>
                              {roleOptions.map((role) => (
                                <SelectItem key={role.value} value={role.value}>
                                  <div className="flex items-center gap-2">
                                    <role.icon className="w-4 h-4" />
                                    <span>{role.label}</span>
                                  </div>
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>

                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => handleRemoveMember(member)}
                            className="text-error-500 hover:bg-error-500/10 ml-2"
                          >
                            <X className="w-4 h-4" />
                          </Button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <div className="text-center py-12">
                  <Users className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                    {t("teams.detail.noMembersTitle")}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginBottom: "1.5rem" }}>
                    {isNew
                      ? t("teams.detail.saveFirst")
                      : t("teams.detail.addFirstMember")}
                  </p>
                  {!isNew && (
                    <Button
                      onClick={handleAddMember}
                      disabled={availableUsers.length === 0}
                      className="bg-brand-500 hover:bg-brand-600"
                    >
                      <Plus className="w-4 h-4 mr-2" />
                      {t("teams.detail.addFirstMemberBtn")}
                    </Button>
                  )}
                </div>
              )}
            </Card>
          </div>
        </div>

        <Dialog open={isAddMemberOpen} onOpenChange={setIsAddMemberOpen}>
          <DialogContent className="bg-card border-border sm:max-w-[600px]">
            <DialogHeader>
              <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
                {t("teams.detail.addMemberTitle")}
              </DialogTitle>
              <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                {t("teams.detail.addMemberDesc")}
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-5 py-4">
              <div>
                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                  {t("teams.detail.userLabel")} <span className="text-error-500">*</span>
                </label>
                <Select value={newMemberId} onValueChange={setNewMemberId}>
                  <SelectTrigger className="bg-input-background">
                    <SelectValue placeholder={t("schedules.selectUser")} />
                  </SelectTrigger>
                  <SelectContent>
                    {availableUsers.filter((user) => user.id).map((user) => {
                      const userColor = getMemberColor(user.displayName ?? user.email);
                      const userInitials = user.initials ?? getInitials(user.displayName ?? user.email);
                      return (
                        <SelectItem key={user.id} value={user.id}>
                          <div className="flex items-center gap-2">
                            <div
                              className="w-6 h-6 rounded-full flex items-center justify-center text-white text-xs font-bold"
                              style={{ backgroundColor: userColor }}
                            >
                              {userInitials}
                            </div>
                            <span>{user.displayName ?? user.email}</span>
                          </div>
                        </SelectItem>
                      );
                    })}
                  </SelectContent>
                </Select>
              </div>

              <div>
                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                  {t("teams.detail.roleLabel")} <span className="text-error-500">*</span>
                </label>
                <div className="space-y-2">
                  {roleOptions.map((role) => (
                    <button
                      key={role.value}
                      onClick={() => setNewMemberRole(role.value)}
                      className={`w-full p-4 rounded-lg border-2 text-left transition-all ${newMemberRole === role.value
                        ? "border-brand-500 bg-brand-500/10"
                        : "border-border hover:border-border-light"
                        }`}
                    >
                      <div className="flex items-start gap-3">
                        <div
                          className={`w-10 h-10 rounded-lg flex items-center justify-center ${newMemberRole === role.value ? "bg-brand-500" : "bg-muted/20"
                            }`}
                        >
                          <role.icon
                            className={`w-5 h-5 ${newMemberRole === role.value ? "text-white" : "text-muted-foreground"
                              }`}
                          />
                        </div>
                        <div className="flex-1">
                          <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                            {role.label}
                          </p>
                          <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                            {role.description}
                          </p>
                        </div>
                      </div>
                    </button>
                  ))}
                </div>
              </div>
            </div>

            <DialogFooter>
              <Button
                variant="outline"
                onClick={() => setIsAddMemberOpen(false)}
                className="bg-input-background"
              >
                {t("common.cancel")}
              </Button>
              <Button
                onClick={handleSubmitAddMember}
                disabled={!newMemberId || addMember.isPending}
                className="bg-brand-500 hover:bg-brand-600 text-white"
              >
                {addMember.isPending ? (
                  <>
                    <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                    {t("teams.detail.adding")}
                  </>
                ) : (
                  <>
                    <Plus className="w-4 h-4 mr-2" />
                    {t("teams.detail.addMember")}
                  </>
                )}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <Dialog open={isRemoveMemberModalOpen} onOpenChange={setIsRemoveMemberModalOpen}>
          <DialogContent className="bg-card border-border sm:max-w-[500px]">
            <DialogHeader>
              <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
                {t("teams.detail.removeMemberTitle")}
              </DialogTitle>
            </DialogHeader>
            <div className="py-4">
              <div className="flex gap-3 mb-4">
                <div className="w-10 h-10 rounded-full bg-warning-500/10 flex items-center justify-center flex-shrink-0">
                  <AlertCircle className="w-5 h-5 text-warning-500" />
                </div>
                <div>
                  <p style={{ fontSize: "0.875rem", marginBottom: "0.5rem" }}>
                    {t("teams.detail.removeMemberMsg").replace("{name}", selectedMember?.name ?? "")}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {t("teams.detail.removeMemberWarn")}
                  </p>
                </div>
              </div>
            </div>
            <DialogFooter>
              <Button
                variant="outline"
                onClick={() => setIsRemoveMemberModalOpen(false)}
                className="bg-input-background"
              >
                {t("common.cancel")}
              </Button>
              <Button
                onClick={handleConfirmRemoveMember}
                disabled={removeMember.isPending}
                className="bg-warning-500 hover:bg-warning-600 text-white"
              >
                {removeMember.isPending ? (
                  <>
                    <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                    {t("teams.detail.removing")}
                  </>
                ) : (
                  <>
                    <X className="w-4 h-4 mr-2" />
                    {t("teams.detail.removeMember")}
                  </>
                )}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <Dialog open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
          <DialogContent className="bg-card border-border sm:max-w-[500px]">
            <DialogHeader>
              <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
                {t("teams.deleteTeamTitle")}
              </DialogTitle>
            </DialogHeader>
            <div className="py-4">
              <div className="flex gap-3 mb-4">
                <div className="w-10 h-10 rounded-full bg-error-500/10 flex items-center justify-center flex-shrink-0">
                  <AlertCircle className="w-5 h-5 text-error-500" />
                </div>
                <div>
                  <p style={{ fontSize: "0.875rem", marginBottom: "0.5rem" }}>
                    {t("teams.detail.deleteTeamMsg").replace("{name}", teamName)}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {t("teams.detail.deleteTeamWarn")}
                  </p>
                </div>
              </div>
            </div>
            <DialogFooter>
              <Button
                variant="outline"
                onClick={() => setIsDeleteModalOpen(false)}
                className="bg-input-background"
              >
                {t("common.cancel")}
              </Button>
              <Button
                onClick={handleDelete}
                disabled={deleteTeamMutation.isPending}
                className="bg-error-500 hover:bg-error-600 text-white"
              >
                {deleteTeamMutation.isPending ? (
                  <>
                    <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                    {t("common.deleting")}
                  </>
                ) : (
                  <>
                    <Trash2 className="w-4 h-4 mr-2" />
                    {t("teams.deleteTeam")}
                  </>
                )}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </>
  );
}