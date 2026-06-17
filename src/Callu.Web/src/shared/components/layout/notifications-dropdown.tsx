/**
 * Notifications Dropdown Menu
 * Shows recent notifications with actions — connected to API
 */

import { Link } from "react-router";
import { Bell, Check, ExternalLink, Settings } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "../ui/dropdown-menu";
import { Button } from "../ui/button";
import { ScrollArea } from "../ui/scroll-area";
import {
  useRecentNotifications,
  useUnreadCount,
  useMarkAsRead,
  useMarkAllAsRead,
} from "@/features/notifications/hooks/use-notifications";
import { t } from "@/shared/locales/i18n";

function getNotificationIcon(type: string) {
  switch (type) {
    case "incident":
      return "🚨";
    case "escalation":
      return "⚠️";
    case "resolved":
      return "✅";
    default:
      return "📢";
  }
}

export function NotificationsDropdown() {
  const { data: notifications } = useRecentNotifications(10);
  const { data: unreadData } = useUnreadCount();
  const markAsReadMutation = useMarkAsRead();
  const markAllAsReadMutation = useMarkAllAsRead();

  const unreadCount = unreadData?.count ?? 0;
  const items = notifications ?? [];

  const handleMarkAsRead = (id: string) => {
    markAsReadMutation.mutate(id);
  };

  const handleMarkAllAsRead = () => {
    markAllAsReadMutation.mutate(undefined);
  };

  const triggerAriaLabel =
    unreadCount > 0
      ? t("a11y.notificationsMenuUnread", { count: String(unreadCount) })
      : t("a11y.notificationsMenu");

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          aria-label={triggerAriaLabel}
          className="relative p-2 rounded-lg hover:bg-surface-light transition-colors"
        >
          <Bell className="w-5 h-5" />
          {unreadCount > 0 && (
            <div className="absolute top-1 right-1 w-2 h-2 rounded-full bg-error-500 animate-pulse" />
          )}
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="w-96 bg-card/95 backdrop-blur-xl border-border"
      >
        <div className="flex items-center justify-between p-4 border-b border-border">
          <div>
            <DropdownMenuLabel className="p-0" style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
              {t("notifications.title")}
            </DropdownMenuLabel>
            {unreadCount > 0 && (
              <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                {t("notifications.unreadCountShort", { count: String(unreadCount) })}
              </p>
            )}
          </div>
          <div className="flex items-center gap-1">
            {unreadCount > 0 && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={handleMarkAllAsRead}
                disabled={markAllAsReadMutation.isPending}
                className="h-7 text-xs"
              >
                <Check className="w-3.5 h-3.5 mr-1" />
                {t("notifications.markAllRead")}
              </Button>
            )}
            <Link to="/notifications">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-7 text-xs"
                aria-label={t("a11y.openNotificationsPage")}
              >
                <Settings className="w-3.5 h-3.5" />
              </Button>
            </Link>
          </div>
        </div>

        <ScrollArea className="h-[400px]">
          {items.length > 0 ? (
            <div className="divide-y divide-border">
              {items.map((notification) => (
                <div
                  key={notification.id}
                  className={`p-4 hover:bg-surface-light/50 transition-colors ${
                    !notification.isRead ? "bg-brand-500/5" : ""
                  }`}
                >
                  <div className="flex items-start gap-3">
                    <div
                      className={`flex-shrink-0 w-8 h-8 rounded-lg flex items-center justify-center text-lg ${
                        !notification.isRead ? "bg-brand-500/10" : "bg-muted"
                      }`}
                    >
                      {getNotificationIcon(notification.type)}
                    </div>

                    <div className="flex-1 min-w-0">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex-1 min-w-0">
                          <p
                            style={{
                              fontSize: "0.875rem",
                              fontWeight: !notification.isRead ? 600 : 500,
                            }}
                            className="truncate"
                          >
                            {notification.title}
                          </p>
                          {notification.message && (
                            <p
                              style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}
                              className="line-clamp-2"
                            >
                              {notification.message}
                            </p>
                          )}
                        </div>
                      </div>

                      <div className="flex items-center justify-between mt-2">
                        <span style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                          {notification.timeAgo}
                        </span>
                        <div className="flex items-center gap-2">
                          {!notification.isRead && (
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => handleMarkAsRead(notification.id)}
                              className="h-6 text-xs"
                            >
                              <Check className="w-3 h-3 mr-1" />
                              {t("notifications.markRead")}
                            </Button>
                          )}
                          {notification.actionUrl && (
                            <Link to={notification.actionUrl}>
                              <Button type="button" variant="ghost" size="sm" className="h-6 text-xs">
                                {t("common.view")}
                                <ExternalLink className="w-3 h-3 ml-1" />
                              </Button>
                            </Link>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <Bell className="w-12 h-12 text-muted-foreground mb-3" />
              <p style={{ fontSize: "0.875rem", fontWeight: 600 }}>{t("notifications.dropdownEmptyTitle")}</p>
              <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                {t("notifications.dropdownEmptySubtitle")}
              </p>
            </div>
          )}
        </ScrollArea>

        {items.length > 0 && (
          <>
            <DropdownMenuSeparator />
            <div className="p-2">
              <Link to="/notifications">
                <Button type="button" variant="ghost" className="w-full justify-center text-xs">
                  {t("notifications.viewAllInMenu")}
                </Button>
              </Link>
            </div>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
