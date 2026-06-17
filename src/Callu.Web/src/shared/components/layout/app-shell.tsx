import { useEffect, useReducer, useState } from "react";
import { Link, useLocation } from "react-router";
import {
  LayoutDashboard,
  TriangleAlert,
  Server,
  Users,
  Calendar,
  Zap,
  Settings,
  Phone,
  Menu,
  X,
  ChevronRight,
  Radio,
  Mail,
  Globe,
  UserCog,
  BarChart3,
  FileSearch,
  BookOpen,
  Video,
  Wrench,
  ScrollText,
  type LucideIcon,
} from "lucide-react";
import { Avatar, AvatarFallback } from "../ui/avatar";
import { NotificationsDropdown } from "./notifications-dropdown";
import { UserDropdown } from "./user-dropdown";
import { PageHelp } from "@/shared/components/help/page-help";
import { useAuth } from "@/shared/auth/auth.context";
import { useSignalR } from "@/shared/signalr";
import { onLocaleChange, t } from "@/shared/locales/i18n";

type NavDef = {
  labelKey: string;
  href: string;
  icon: LucideIcon;
  badge?: string | number;
  groupKey?: string;
  subItems?: { labelKey: string; href: string; icon: LucideIcon }[];
};

const NAV_DEFS: NavDef[] = [
  { labelKey: "nav.dashboard", href: "/dashboard", icon: LayoutDashboard, groupKey: "nav.groupRespond" },
  { labelKey: "nav.incidents", href: "/incidents", icon: TriangleAlert },
  { labelKey: "nav.conferences", href: "/conferences", icon: Video },

  { labelKey: "nav.services", href: "/services", icon: Server, groupKey: "nav.groupConfigure" },
  { labelKey: "nav.escalations", href: "/escalations", icon: Zap },
  { labelKey: "nav.schedules", href: "/schedules", icon: Calendar },
  { labelKey: "nav.teams", href: "/teams", icon: Users },

  { labelKey: "nav.reports", href: "/reports", icon: BarChart3, groupKey: "nav.groupAnalyze" },
  { labelKey: "nav.postmortems", href: "/postmortems", icon: FileSearch },
  { labelKey: "nav.runbooks", href: "/runbooks", icon: BookOpen },
  { labelKey: "nav.callLogs", href: "/call-logs", icon: Phone },
  { labelKey: "nav.maintenance", href: "/maintenance", icon: Wrench },
  { labelKey: "nav.auditLog", href: "/audit-logs", icon: ScrollText },

  {
    labelKey: "nav.settings",
    href: "/settings",
    icon: Settings,
    groupKey: "nav.groupSettings",
    subItems: [
      { labelKey: "nav.userManagement", href: "/users", icon: UserCog },
      { labelKey: "nav.communications", href: "/settings/communications", icon: Radio },
      { labelKey: "nav.emailTemplates", href: "/settings/email-templates", icon: Mail },
      { labelKey: "nav.statusPage", href: "/settings/status-page", icon: Globe },
    ],
  },
];

interface NavItemsProps {
  showLabels: boolean;
  onNavigate?: () => void;
}

function NavItems({ showLabels, onNavigate }: NavItemsProps) {
  const location = useLocation();

  const isSubActive = (href: string) =>
    location.pathname === href || location.pathname.startsWith(`${href}/`);

  return (
    <>
      {NAV_DEFS.map((item, index) => {
        const Icon = item.icon;
        const selfActive = isSubActive(item.href);
        const childActive = item.subItems?.some((sub) => isSubActive(sub.href)) ?? false;
        const navEntryActive = selfActive || childActive;

        return (
          <div key={item.href}>
            {item.groupKey && (
              <div className={`${index > 0 ? "mt-4 pt-4 border-t border-sidebar-border" : ""}`}>
                {showLabels && (
                  <p className="px-3 mb-2 text-[0.6875rem] font-semibold uppercase tracking-wider text-muted-foreground/60">
                    {t(item.groupKey)}
                  </p>
                )}
              </div>
            )}
            <Link
              to={item.href}
              onClick={onNavigate}
              className={`relative group flex items-center gap-3 px-3 py-2.5 rounded-lg transition-all ${
                navEntryActive
                  ? "bg-sidebar-accent text-sidebar-accent-foreground shadow-lg shadow-brand-500/10"
                  : "text-sidebar-foreground hover:bg-sidebar-accent/50"
              }`}
            >
              <Icon className={`w-5 h-5 flex-shrink-0 ${navEntryActive ? "text-brand-500" : ""}`} />
              {showLabels && (
                <>
                  <span className={`text-sm ${navEntryActive ? "font-semibold" : "font-medium"}`}>
                    {t(item.labelKey)}
                  </span>
                  {item.badge && (
                    <span className="ml-auto px-2 py-0.5 rounded-full bg-error-500 text-white text-xs font-semibold animate-pulse">
                      {item.badge}
                    </span>
                  )}
                </>
              )}
              {!showLabels && item.badge && (
                <div className="absolute left-8 top-1.5 w-2 h-2 rounded-full bg-error-500 animate-pulse" />
              )}
            </Link>
            {item.subItems && (
              <div
                className={
                  showLabels
                    ? "mt-1 ml-2 pl-3 border-l border-sidebar-border/70 space-y-1"
                    : "mt-1 flex flex-col items-center gap-1"
                }
                role="group"
                aria-label={t(item.labelKey)}
              >
                {item.subItems.map((subItem) => {
                  const SubIcon = subItem.icon;
                  const subActive = isSubActive(subItem.href);

                  return (
                    <Link
                      key={subItem.href}
                      to={subItem.href}
                      onClick={onNavigate}
                      className={`group flex items-center gap-3 px-3 py-2 rounded-lg transition-all w-full ${
                        subActive
                          ? "bg-sidebar-accent text-sidebar-accent-foreground shadow-lg shadow-brand-500/10"
                          : "text-sidebar-foreground hover:bg-sidebar-accent/50"
                      }`}
                    >
                      <SubIcon className={`w-5 h-5 flex-shrink-0 ${subActive ? "text-brand-500" : ""}`} />
                      {showLabels && (
                        <span className={`text-sm ${subActive ? "font-semibold" : "font-medium"}`}>
                          {t(subItem.labelKey)}
                        </span>
                      )}
                    </Link>
                  );
                })}
              </div>
            )}
          </div>
        );
      })}

    </>
  );
}

function SidebarUserProfile({ showLabels }: { showLabels: boolean }) {
  const { user } = useAuth();

  const displayName = user?.name || t("common.fallbackUser");
  const displayEmail = user?.email || "";
  const initials = displayName
    .split(" ")
    .map((n) => n[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);

  return (
    <div className={`flex items-center gap-3 p-3 rounded-lg bg-sidebar-accent/30 ${showLabels ? "" : "justify-center"}`}>
      <Avatar className="w-9 h-9 border-2 border-brand-500">
        <AvatarFallback className="bg-gradient-to-br from-brand-500 to-brand-600 text-white font-semibold">
          {initials}
        </AvatarFallback>
      </Avatar>
      {showLabels && (
        <div className="flex-1 min-w-0">
          <p className="text-sm font-semibold truncate">{displayName}</p>
          <p className="text-xs text-muted-foreground truncate">{displayEmail}</p>
        </div>
      )}
    </div>
  );
}

function SidebarLogo({ showLabels }: { showLabels: boolean }) {
  return (
    <Link to="/dashboard" className="flex items-center gap-3">
      {showLabels ? (
        <img src="/callu-logo.png" alt="Callu" className="h-9 w-auto flex-shrink-0" />
      ) : (
        <img src="/callu-icon.png" alt="Callu" className="w-10 h-10 flex-shrink-0 object-contain" />
      )}
    </Link>
  );
}

interface AppShellProps {
  children: React.ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  useSignalR();

  const [, bumpLocale] = useReducer((n: number) => n + 1, 0);
  useEffect(() => onLocaleChange(() => bumpLocale()), []);

  const [isSidebarOpen, setIsSidebarOpen] = useState(true);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  return (
    <div className="min-h-screen bg-background">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:z-[100] focus:top-4 focus:left-4 focus:px-4 focus:py-2 focus:rounded-lg focus:bg-brand-500 focus:text-white focus:outline-none"
      >
        {t("a11y.skipToContent")}
      </a>
      <aside
        className={`fixed top-0 left-0 h-screen bg-sidebar/95 backdrop-blur-md border-r border-sidebar-border transition-all duration-300 z-40 hidden lg:block ${
          isSidebarOpen ? "w-64" : "w-20"
        }`}
      >
        <div className="flex flex-col h-full">
          <div className="p-6 border-b border-sidebar-border">
            <SidebarLogo showLabels={isSidebarOpen} />
          </div>

          <nav className="flex-1 p-4 space-y-1 overflow-y-auto" aria-label={t("a11y.mainNavigation")}>
            <NavItems showLabels={isSidebarOpen} />
          </nav>

          <div className="p-4 border-t border-sidebar-border">
            <SidebarUserProfile showLabels={isSidebarOpen} />
          </div>

          <button
            type="button"
            onClick={() => setIsSidebarOpen(!isSidebarOpen)}
            aria-expanded={isSidebarOpen}
            aria-label={isSidebarOpen ? t("a11y.collapseSidebar") : t("a11y.expandSidebar")}
            className="absolute -right-3 top-20 w-6 h-6 rounded-full bg-brand-500 text-white flex items-center justify-center shadow-lg hover:bg-brand-600 transition-colors"
          >
            <ChevronRight className={`w-4 h-4 transition-transform ${isSidebarOpen ? "rotate-180" : ""}`} />
          </button>
        </div>
      </aside>

      {isMobileMenuOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm"
            onClick={() => setIsMobileMenuOpen(false)}
            onKeyDown={(e) => e.key === "Escape" && setIsMobileMenuOpen(false)}
            role="presentation"
          />

          <aside
            className="absolute top-0 left-0 h-full w-64 bg-sidebar/95 backdrop-blur-md border-r border-sidebar-border"
            aria-label={t("a11y.mainNavigation")}
          >
            <div className="flex flex-col h-full">
              <div className="flex items-center justify-between p-6 border-b border-sidebar-border">
                <SidebarLogo showLabels />
                <button
                  type="button"
                  onClick={() => setIsMobileMenuOpen(false)}
                  aria-label={t("a11y.closeMenu")}
                  className="text-sidebar-foreground hover:text-brand-500"
                >
                  <X className="w-6 h-6" />
                </button>
              </div>

              <nav className="flex-1 p-4 space-y-1 overflow-y-auto">
                <NavItems showLabels onNavigate={() => setIsMobileMenuOpen(false)} />
              </nav>
            </div>
          </aside>
        </div>
      )}

      <div className={`transition-all duration-300 ${isSidebarOpen ? "lg:ml-64" : "lg:ml-20"}`}>
        <header className="sticky top-0 z-30 bg-card/80 backdrop-blur-xl border-b border-border">
          <div className="flex items-center justify-between px-4 lg:px-6 h-16">
            <button
              type="button"
              onClick={() => setIsMobileMenuOpen(true)}
              aria-label={t("a11y.openMenu")}
              className="lg:hidden text-foreground"
            >
              <Menu className="w-6 h-6" />
            </button>

            <div className="flex items-center gap-2 ml-auto">
              <PageHelp />
              <NotificationsDropdown />
              <UserDropdown />
            </div>
          </div>
        </header>

        <main id="main-content" className="min-h-[calc(100vh-4rem)]" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
}
