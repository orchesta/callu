/**
 * User Profile Dropdown Menu
 * Shows user info and navigation
 */

import { Link } from "react-router";
import { User, Settings, LogOut, Shield } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "../ui/dropdown-menu";
import { Avatar, AvatarFallback } from "../ui/avatar";
import { authService } from "@/shared/auth/auth.service";
import { useAuth } from "@/shared/auth/auth.context";
import { t } from "@/shared/locales/i18n";

function translateRole(role: string): string {
  const key = `roles.${role}`;
  const label = t(key);
  return label === key ? role : label;
}

export function UserDropdown() {
  const { user: authUser } = useAuth();

  const displayName = authUser?.name || t("common.fallbackUser");
  const initials =
    displayName
      .split(" ")
      .map((n) => n[0])
      .join("")
      .toUpperCase()
      .slice(0, 2) || "U";

  const rawRole = authUser?.role ?? "Member";
  const currentUser = {
    name: displayName,
    email: authUser?.email ?? "",
    role: translateRole(rawRole),
    roleKey: rawRole,
    initials,
  };

  const handleLogout = async () => {
    try {
      await authService.logout();
    } catch (error) {
      console.error("Logout error:", error);
      window.location.href = "/login";
    }
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          aria-label={t("a11y.userMenu")}
          className="flex items-center gap-2 p-1 rounded-lg hover:bg-surface-light transition-colors"
        >
          <Avatar className="w-8 h-8 border-2 border-brand-500">
            <AvatarFallback className="bg-gradient-to-br from-brand-500 to-brand-600 text-white text-xs font-semibold">
              {currentUser.initials}
            </AvatarFallback>
          </Avatar>
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="w-64 bg-card/95 backdrop-blur-xl border-border"
      >
        <div className="p-3 border-b border-border">
          <div className="flex items-center gap-3">
            <Avatar className="w-12 h-12 border-2 border-brand-500">
              <AvatarFallback className="bg-gradient-to-br from-brand-500 to-brand-600 text-white font-semibold">
                {currentUser.initials}
              </AvatarFallback>
            </Avatar>
            <div className="flex-1 min-w-0">
              <p style={{ fontSize: "0.875rem", fontWeight: 600 }} className="truncate">
                {currentUser.name}
              </p>
              <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }} className="truncate">
                {currentUser.email}
              </p>
              <p
                style={{
                  fontSize: "0.75rem",
                  color: "#3E7BFA",
                  marginTop: "0.25rem",
                  fontWeight: 500,
                }}
              >
                {currentUser.role}
              </p>
            </div>
          </div>
        </div>

        <div className="py-1">
          <Link to="/profile">
            <DropdownMenuItem className="cursor-pointer">
              <User className="w-4 h-4 mr-3" />
              <span style={{ fontSize: "0.875rem" }}>{t("userDropdown.myProfile")}</span>
            </DropdownMenuItem>
          </Link>

          <Link to="/settings">
            <DropdownMenuItem className="cursor-pointer">
              <Settings className="w-4 h-4 mr-3" />
              <span style={{ fontSize: "0.875rem" }}>{t("userDropdown.accountSettings")}</span>
            </DropdownMenuItem>
          </Link>
        </div>

        <DropdownMenuSeparator />

        {currentUser.roleKey === "Admin" && (
          <>
            <div className="py-1">
              <DropdownMenuLabel style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                {t("userDropdown.administration")}
              </DropdownMenuLabel>
              <Link to="/users">
                <DropdownMenuItem className="cursor-pointer">
                  <Shield className="w-4 h-4 mr-3" />
                  <span style={{ fontSize: "0.875rem" }}>{t("nav.userManagement")}</span>
                </DropdownMenuItem>
              </Link>
              <Link to="/settings">
                <DropdownMenuItem className="cursor-pointer">
                  <Settings className="w-4 h-4 mr-3" />
                  <span style={{ fontSize: "0.875rem" }}>{t("userDropdown.systemSettings")}</span>
                </DropdownMenuItem>
              </Link>
            </div>
            <DropdownMenuSeparator />
          </>
        )}

        <div className="py-1">
          <DropdownMenuItem
            className="cursor-pointer text-error-500 focus:text-error-500 focus:bg-error-500/10"
            onClick={handleLogout}
          >
            <LogOut className="w-4 h-4 mr-3" />
            <span style={{ fontSize: "0.875rem", fontWeight: 500 }}>{t("auth.logout")}</span>
          </DropdownMenuItem>
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
