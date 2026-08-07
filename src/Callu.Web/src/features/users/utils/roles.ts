import { t } from "@/shared/locales/i18n";

export const roleOptions = ["Admin", "TeamLead", "Member", "Viewer", "Auditor"];

/**
 * Resolved once at import, so a locale switch does not restyle these until reload — the same as
 * when this lived in the page component. The role names themselves are identifiers, not copy.
 */
export const roleDescriptions: Record<string, string> = {
  Admin: t("users.roleDescAdmin"),
  TeamLead: t("users.roleDescTeamLead"),
  Member: t("users.roleDescMember"),
  Viewer: t("users.roleDescViewer"),
  Auditor: t("users.roleDescAuditor"),
};
