import { useState, useMemo } from "react";
import { t } from "@/shared/locales/i18n";
import { toast } from "@/shared/utils/toast";
import { Users, UserPlus, Search, Mail, Trash2, Edit, UserCheck, Clock } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { motion } from "motion/react";
import {
  useUsers,
  useInviteUser,
  useUpdateUser,
  useChangeRole,
  useRemoveUser,
  useResendInvitation,
} from "../hooks/use-users";
import type { UserDto, AdminUpdateUserRequest } from "../types/user.types";
import { getAvatarColor, getUserInitials, getUserFullName } from "../utils/user-display";
import { roleOptions } from "../utils/roles";
import { InviteUserModal } from "./invite-user-modal";
import { InviteLinkDialog } from "./invite-link-dialog";
import { EditUserModal } from "./edit-user-modal";
import { DeleteConfirmModal } from "./delete-confirm-modal";

/** Admin-only user management page: list, invite, edit, remove, change role. */

const statusOptions = ["All", "Active", "Pending"];

export function UsersPage() {
  const { data: users, isLoading, error } = useUsers();
  const inviteUserMutation = useInviteUser();
  const [pendingInvite, setPendingInvite] = useState<{ email: string; link: string } | null>(null);
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
      {
        onSuccess: (result) => {
          setIsInviteModalOpen(false);
          if (result.emailSent) {
            toast.success(result.message);
            return;
          }
          // The account exists but nothing reached the invitee; the link is all they have.
          setPendingInvite({ email, link: result.inviteLink ?? "" });
        },
      },
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
    resendInvitationMutation.mutate(userId, {
      onSuccess: (result) => {
        if (result.emailSent) {
          toast.success(result.message);
          return;
        }
        const user = (users ?? []).find((u) => u.id === userId);
        setPendingInvite({ email: user?.email ?? "", link: result.inviteLink ?? "" });
      },
    });
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
      {pendingInvite && (
        <InviteLinkDialog
          email={pendingInvite.email}
          link={pendingInvite.link}
          onClose={() => setPendingInvite(null)}
        />
      )}
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
