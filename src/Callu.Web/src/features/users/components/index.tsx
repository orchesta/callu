import { useState, useMemo } from "react";
import { t } from "@/shared/locales/i18n";
import { Users, UserPlus, Search, Mail, Trash2, Edit, UserCheck, Clock, Crown, Shield, Eye } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { motion, AnimatePresence } from "motion/react";
import {
  useUsers,
  useInviteUser,
  useUpdateUser,
  useChangeRole,
  useRemoveUser,
  useResendInvitation,
} from "../hooks/use-users";
import type { UserDto, AdminUpdateUserRequest } from "../types/user.types";

/**
 * User Management Page
 *
 * Admin-only page for managing users:
 * - View all users with stats
 * - Search and filter users
 * - Invite new users
 * - Edit user details
 * - Remove users
 * - Change user roles
 */

const roleOptions = ["Admin", "TeamLead", "Member", "Viewer"];
const statusOptions = ["All", "Active", "Pending"];

const roleDescriptions: Record<string, string> = {
  Admin: t("users.roleDescAdmin"),
  TeamLead: t("users.roleDescTeamLead"),
  Member: t("users.roleDescMember"),
  Viewer: t("users.roleDescViewer"),
};

const avatarColors = [
  "bg-brand-500",
  "bg-purple-500",
  "bg-emerald-500",
  "bg-amber-500",
  "bg-rose-500",
  "bg-teal-500",
  "bg-indigo-500",
  "bg-cyan-500",
];

function getAvatarColor(id: string): string {
  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = id.charCodeAt(i) + ((hash << 5) - hash);
  return avatarColors[Math.abs(hash) % avatarColors.length];
}

function getUserInitials(user: UserDto): string {
  if (user.initials) return user.initials;
  const first = user.firstName?.charAt(0) || "";
  const last = user.lastName?.charAt(0) || "";
  if (first || last) return `${first}${last}`.toUpperCase();
  return user.email.charAt(0).toUpperCase();
}

function getUserFullName(user: UserDto): string {
  if (user.displayName) return user.displayName;
  if (user.firstName || user.lastName) return `${user.firstName ?? ""} ${user.lastName ?? ""}`.trim();
  return user.email;
}

export function UsersPage() {
  const { data: users, isLoading, error } = useUsers();
  const inviteUserMutation = useInviteUser();
  const updateUserMutation = useUpdateUser();
  const changeRoleMutation = useChangeRole();
  const removeUserMutation = useRemoveUser();
  const resendInvitationMutation = useResendInvitation();

  const [searchQuery, setSearchQuery] = useState("");
  const [roleFilter, setRoleFilter] = useState("All");
  const [statusFilter, setStatusFilter] = useState("All");
  const [isInviteModalOpen, setIsInviteModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [selectedUser, setSelectedUser] = useState<UserDto | null>(null);
  const [isDeleteConfirmOpen, setIsDeleteConfirmOpen] = useState(false);
  const [userToDelete, setUserToDelete] = useState<UserDto | null>(null);

  const filteredUsers = useMemo(() => {
    if (!users) return [];
    return users.filter((user) => {
      const fullName = getUserFullName(user);
      const matchesSearch =
        fullName.toLowerCase().includes(searchQuery.toLowerCase()) ||
        user.email.toLowerCase().includes(searchQuery.toLowerCase());
      const matchesRole = roleFilter === "All" || user.role === roleFilter;
      const matchesStatus =
        statusFilter === "All" ||
        (statusFilter === "Active" && user.emailConfirmed) ||
        (statusFilter === "Pending" && !user.emailConfirmed);
      return matchesSearch && matchesRole && matchesStatus;
    });
  }, [users, searchQuery, roleFilter, statusFilter]);

  const stats = useMemo(
    () => ({
      total: (users ?? []).length,
      active: (users ?? []).filter((u) => u.emailConfirmed).length,
      pending: (users ?? []).filter((u) => !u.emailConfirmed).length,
    }),
    [users],
  );

  const handleInviteUser = (email: string, role: string) => {
    inviteUserMutation.mutate(
      { email, role },
      { onSuccess: () => setIsInviteModalOpen(false) },
    );
  };

  const handleChangeRole = (userId: string, newRole: string) => {
    changeRoleMutation.mutate({ id: userId, role: newRole });
  };

  const handleUpdateUser = (userId: string, data: AdminUpdateUserRequest) => {
    updateUserMutation.mutate(
      { id: userId, data },
      {
        onSuccess: () => {
          setIsEditModalOpen(false);
          setSelectedUser(null);
        },
      },
    );
  };

  const handleResendInvite = (userId: string) => {
    resendInvitationMutation.mutate(userId);
  };

  const handleDeleteUser = () => {
    if (!userToDelete) return;
    removeUserMutation.mutate(userToDelete.id, {
      onSuccess: () => {
        setIsDeleteConfirmOpen(false);
        setUserToDelete(null);
      },
    });
  };

  if (isLoading) {
    return <LoadingState message={t("users.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("users.loadFailed")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <div className="p-6">
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold text-white">{t("users.title")}</h1>
            <p className="mt-2 text-gray-400">{t("users.subtitle")}</p>
          </div>
          <button
            onClick={() => setIsInviteModalOpen(true)}
            className="flex items-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-brand-600"
          >
            <UserPlus className="h-4 w-4" />
            {t("users.inviteUser")}
          </button>
        </div>

        <div className="grid gap-6 md:grid-cols-3">
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl"
          >
            <div className="flex items-center gap-4">
              <div className="rounded-lg bg-brand-500/20 p-3">
                <Users className="h-6 w-6 text-brand-400" />
              </div>
              <div>
                <p className="text-sm text-gray-400">{t("users.totalUsers")}</p>
                <p className="text-2xl font-bold text-white">{stats.total}</p>
              </div>
            </div>
          </motion.div>

          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.1 }}
            className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl"
          >
            <div className="flex items-center gap-4">
              <div className="rounded-lg bg-green-500/20 p-3">
                <UserCheck className="h-6 w-6 text-green-400" />
              </div>
              <div>
                <p className="text-sm text-gray-400">{t("common.active")}</p>
                <p className="text-2xl font-bold text-white">{stats.active}</p>
              </div>
            </div>
          </motion.div>

          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.2 }}
            className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl"
          >
            <div className="flex items-center gap-4">
              <div className="rounded-lg bg-amber-500/20 p-3">
                <Clock className="h-6 w-6 text-amber-400" />
              </div>
              <div>
                <p className="text-sm text-gray-400">{t("users.pendingInvites")}</p>
                <p className="text-2xl font-bold text-white">{stats.pending}</p>
              </div>
            </div>
          </motion.div>
        </div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.3 }}
          className="rounded-xl border border-white/10 bg-white/5 p-4 backdrop-blur-xl"
        >
          <div className="grid gap-4 md:grid-cols-3">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t("users.searchPlaceholder")}
                className="w-full rounded-lg border border-white/10 bg-white/5 py-2 pl-10 pr-4 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
              />
            </div>

            <select
              value={roleFilter}
              onChange={(e) => setRoleFilter(e.target.value)}
              className="rounded-lg border border-white/10 bg-white/5 px-4 py-2 text-white backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
            >
              <option value="All" className="bg-gray-900">{t("users.allRoles")}</option>
              {roleOptions.map((role) => (
                <option key={role} value={role} className="bg-gray-900">
                  {role}
                </option>
              ))}
            </select>

            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              className="rounded-lg border border-white/10 bg-white/5 px-4 py-2 text-white backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
            >
              {statusOptions.map((status) => (
                <option key={status} value={status} className="bg-gray-900">
                  {status}
                </option>
              ))}
            </select>
          </div>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.4 }}
          className="overflow-hidden rounded-xl border border-white/10 bg-white/5 backdrop-blur-xl"
        >
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-white/10 bg-white/5">
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">{t("users.colUser")}</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">{t("users.colEmail")}</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">{t("users.colRole")}</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">{t("common.status")}</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">{t("users.colJoined")}</th>
                  <th className="px-6 py-4 text-right text-sm font-semibold text-gray-300">{t("users.colActions")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-white/10">
                {filteredUsers.map((user) => (
                  <tr key={user.id} className="transition-colors hover:bg-white/5">
                    <td className="px-6 py-4">
                      <div className="flex items-center gap-3">
                        <div
                          className={`flex h-10 w-10 items-center justify-center rounded-full text-sm font-bold text-white ${getAvatarColor(user.id)}`}
                        >
                          {getUserInitials(user)}
                        </div>
                        <div>
                          <p className="font-medium text-white">{getUserFullName(user)}</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 text-gray-400">{user.email}</td>
                    <td className="px-6 py-4">
                      <select
                          value={user.role}
                          onChange={(e) => handleChangeRole(user.id, e.target.value)}
                          className="rounded-lg border border-white/10 bg-white/5 px-3 py-1 text-sm text-white transition-colors hover:bg-white/10"
                        >
                          {roleOptions.map((role) => (
                            <option key={role} value={role} className="bg-gray-900">
                              {role}
                            </option>
                          ))}
                        </select>
                    </td>
                    <td className="px-6 py-4">
                      {user.emailConfirmed ? (
                        <span className="inline-flex items-center gap-1 rounded-full bg-green-500/20 px-3 py-1 text-sm font-medium text-green-300">
                          <div className="h-1.5 w-1.5 rounded-full bg-green-400" />
                          {t("common.active")}
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 rounded-full bg-amber-500/20 px-3 py-1 text-sm font-medium text-amber-300">
                          <div className="h-1.5 w-1.5 rounded-full bg-amber-400" />
                          {t("common.pending")}
                        </span>
                      )}
                    </td>
                    <td className="px-6 py-4 text-gray-400">
                      {new Date(user.createdAt).toLocaleDateString("en-US", {
                        month: "short",
                        day: "numeric",
                        year: "numeric",
                      })}
                    </td>
                    <td className="px-6 py-4">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => {
                            setSelectedUser(user);
                            setIsEditModalOpen(true);
                          }}
                          className="rounded-lg p-2 text-gray-400 transition-colors hover:bg-white/10 hover:text-white"
                          title={t("users.editUser")}
                        >
                          <Edit className="h-4 w-4" />
                        </button>
                        {!user.emailConfirmed && (
                          <button
                            onClick={() => handleResendInvite(user.id)}
                            className="rounded-lg p-2 text-gray-400 transition-colors hover:bg-white/10 hover:text-white"
                            title={t("users.resendInvitation")}
                          >
                            <Mail className="h-4 w-4" />
                          </button>
                        )}
                        {user.role !== "Admin" && (
                          <button
                            onClick={() => {
                              setUserToDelete(user);
                              setIsDeleteConfirmOpen(true);
                            }}
                            className="rounded-lg p-2 text-red-400 transition-colors hover:bg-red-500/10 hover:text-red-300"
                            title={t("users.removeUser")}
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {filteredUsers.length === 0 && (
            <div className="py-12 text-center">
              <Users className="mx-auto h-12 w-12 text-gray-600" />
              <p className="mt-4 text-gray-400">{t("users.noUsersFound")}</p>
            </div>
          )}
        </motion.div>
      </div>

      <InviteUserModal
        isOpen={isInviteModalOpen}
        onClose={() => setIsInviteModalOpen(false)}
        onInvite={handleInviteUser}
        isPending={inviteUserMutation.isPending}
      />
      {selectedUser && (
        <EditUserModal
          isOpen={isEditModalOpen}
          onClose={() => {
            setIsEditModalOpen(false);
            setSelectedUser(null);
          }}
          user={selectedUser}
          onChangeRole={handleChangeRole}
          onUpdateUser={handleUpdateUser}
          isSaving={updateUserMutation.isPending}
        />
      )}
      <DeleteConfirmModal
        isOpen={isDeleteConfirmOpen}
        onClose={() => {
          setIsDeleteConfirmOpen(false);
          setUserToDelete(null);
        }}
        user={userToDelete}
        onConfirm={handleDeleteUser}
        isPending={removeUserMutation.isPending}
      />
    </div>
  );
}

function InviteUserModal({
  isOpen,
  onClose,
  onInvite,
  isPending,
}: {
  isOpen: boolean;
  onClose: () => void;
  onInvite: (email: string, role: string) => void;
  isPending: boolean;
}) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState("Member");

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (isPending) return;
    onInvite(email, role);
  };

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => !isPending && onClose()}
            className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm"
          />
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.95 }}
            className="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-white/10 bg-gray-900 p-6 shadow-2xl"
          >
            <h2 className="text-2xl font-bold text-white">{t("users.inviteNewUser")}</h2>
            <p className="mt-2 text-gray-400">{t("users.inviteDescription")}</p>

            <form onSubmit={handleSubmit} className="mt-6 space-y-4">
              <div>
                <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.emailAddress")}</label>
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder={t("users.emailInvitePlaceholder")}
                  required
                  className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                />
              </div>

              <div>
                <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.colRole")}</label>
                <div className="space-y-2">
                  {roleOptions.map((roleOption) => (
                    <label
                      key={roleOption}
                      className={`flex cursor-pointer items-start gap-3 rounded-lg border-2 p-4 transition-colors ${role === roleOption
                        ? "border-brand-500 bg-brand-500/10"
                        : "border-white/10 bg-white/5 hover:bg-white/10"
                        }`}
                    >
                      <input
                        type="radio"
                        name="role"
                        value={roleOption}
                        checked={role === roleOption}
                        onChange={(e) => setRole(e.target.value)}
                        className="mt-1"
                      />
                      <div className="flex-1">
                        <div className="flex items-center gap-2">
                          {roleOption === "Admin" && <Crown className="h-4 w-4 text-red-400" />}
                          {roleOption === "TeamLead" && <Shield className="h-4 w-4 text-purple-400" />}
                          {roleOption === "Member" && <Shield className="h-4 w-4 text-brand-400" />}
                          {roleOption === "Viewer" && <Eye className="h-4 w-4 text-gray-400" />}
                          <p className="font-medium text-white">{roleOption}</p>
                        </div>
                        <p className="mt-1 text-sm text-gray-400">{roleDescriptions[roleOption]}</p>
                      </div>
                    </label>
                  ))}
                </div>
              </div>

              <div className="flex items-center gap-3 pt-4">
                <button
                  type="button"
                  onClick={onClose}
                  className="flex-1 rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 font-medium text-white transition-colors hover:bg-white/10"
                >
                  {t("common.cancel")}
                </button>
                <button
                  type="submit"
                  disabled={isPending || !email}
                  className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-brand-600 disabled:opacity-50"
                >
                  {isPending ? (
                    <>
                      <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                      {t("users.sending")}
                    </>
                  ) : (
                    <>
                      <Mail className="h-4 w-4" />
                      {t("users.sendInvitation")}
                    </>
                  )}
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}

function EditUserModal({
  isOpen,
  onClose,
  user,
  onChangeRole,
  onUpdateUser,
  isSaving,
}: {
  isOpen: boolean;
  onClose: () => void;
  user: UserDto;
  onChangeRole: (userId: string, newRole: string) => void;
  onUpdateUser: (userId: string, data: AdminUpdateUserRequest) => void;
  isSaving: boolean;
}) {
  const [firstName, setFirstName] = useState(user.firstName ?? "");
  const [lastName, setLastName] = useState(user.lastName ?? "");
  const [phoneNumber, setPhoneNumber] = useState(user.phoneNumber ?? "");
  const [role, setRole] = useState(user.role);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    const roleChanged = role !== user.role && user.role !== "Admin";
    const profileChanged =
      firstName.trim() !== (user.firstName ?? "") ||
      lastName.trim() !== (user.lastName ?? "") ||
      phoneNumber.trim() !== (user.phoneNumber ?? "");

    if (roleChanged) onChangeRole(user.id, role);

    if (profileChanged) {
      onUpdateUser(user.id, {
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        phoneNumber: phoneNumber.trim(),
      });
    } else {
      onClose();
    }
  };

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm"
          />
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.95 }}
            className="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-white/10 bg-gray-900 p-6 shadow-2xl"
          >
            <div className="mb-6">
              <h2 className="text-2xl font-bold text-white">{t("users.editUser")}</h2>
              <div className="mt-4 flex items-center gap-3">
                <div className={`flex h-12 w-12 items-center justify-center rounded-full text-lg font-bold text-white ${getAvatarColor(user.id)}`}>
                  {getUserInitials(user)}
                </div>
                <div>
                  <p className="font-medium text-white">{getUserFullName(user)}</p>
                  <p className="text-sm text-gray-400">{user.email}</p>
                </div>
              </div>
            </div>

            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.firstName")}</label>
                  <input
                    type="text"
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    placeholder={t("users.firstName")}
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                  />
                </div>
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.lastName")}</label>
                  <input
                    type="text"
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    placeholder={t("users.lastName")}
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                  />
                </div>
              </div>

              <div>
                <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.phoneNumber")}</label>
                <input
                  type="tel"
                  value={phoneNumber}
                  onChange={(e) => setPhoneNumber(e.target.value)}
                  placeholder="+905551112233"
                  className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 font-mono text-white placeholder-gray-500 backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                />
                <div className="mt-2 flex items-start gap-2">
                  <Shield className="h-4 w-4 text-gray-400 mt-0.5 shrink-0" />
                  <p className="text-xs text-gray-400">{t("users.phoneCallHint")}</p>
                </div>
              </div>

              {user.role !== "Admin" && (
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-300">{t("users.colRole")}</label>
                  <select
                    value={role}
                    onChange={(e) => setRole(e.target.value)}
                    className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 text-white backdrop-blur-xl transition-colors focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
                  >
                    {roleOptions.map((roleOption) => (
                      <option key={roleOption} value={roleOption} className="bg-gray-900">
                        {roleOption}
                      </option>
                    ))}
                  </select>
                </div>
              )}

              <div className="flex items-center gap-3 pt-4">
                <button
                  type="button"
                  onClick={onClose}
                  className="flex-1 rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 font-medium text-white transition-colors hover:bg-white/10"
                >
                  {t("common.cancel")}
                </button>
                <button
                  type="submit"
                  disabled={isSaving}
                  className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-brand-600 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {isSaving && <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />}
                  {t("common.saveChanges")}
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}

function DeleteConfirmModal({
  isOpen,
  onClose,
  user,
  onConfirm,
  isPending,
}: {
  isOpen: boolean;
  onClose: () => void;
  user: UserDto | null;
  onConfirm: () => void;
  isPending: boolean;
}) {
  if (!user) return null;

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm"
          />
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.95 }}
            className="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border border-red-500/20 bg-gray-900 p-6 shadow-2xl"
          >
            <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-red-500/20">
              <Trash2 className="h-6 w-6 text-red-400" />
            </div>
            <h2 className="text-2xl font-bold text-white">{t("users.removeUserConfirm")}</h2>
            <p className="mt-2 text-gray-400">
              {t("users.removeUserMessage")} <strong className="text-white">{getUserFullName(user)}</strong>
            </p>

            <div className="mt-6 flex items-center gap-3">
              <button
                type="button"
                onClick={onClose}
                className="flex-1 rounded-lg border border-white/10 bg-white/5 px-4 py-2.5 font-medium text-white transition-colors hover:bg-white/10"
              >
                {t("common.cancel")}
              </button>
              <button
                onClick={onConfirm}
                disabled={isPending}
                className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-red-500 px-4 py-2.5 font-medium text-white transition-colors hover:bg-red-600 disabled:opacity-50"
              >
                {isPending ? (
                  <>
                    <div className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                    {t("users.removing")}
                  </>
                ) : (
                  <>
                    <Trash2 className="h-4 w-4" />
                    {t("users.removeUser")}
                  </>
                )}
              </button>
            </div>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}