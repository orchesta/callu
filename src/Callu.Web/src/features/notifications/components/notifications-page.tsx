/**
 * Notifications Page
 * Full page view of all notifications with filtering and management
 */

import { useState, useMemo } from 'react';
import { t } from '@/shared/locales/i18n';
import { Link } from 'react-router';
import { Button } from '@/shared/components/ui/button';
import { Card } from '@/shared/components/ui/card';
import { Badge } from '@/shared/components/ui/badge';
import { Tabs, TabsList, TabsTrigger } from '@/shared/components/ui/tabs';
import {
  Bell,
  Check,
  CheckCheck,
  ExternalLink,
  Settings,
  AlertTriangle,
  CheckCircle,
  ArrowUp,
  Loader2,
  AlertCircle,
} from 'lucide-react';
import {
  useRecentNotifications,
  useUnreadCount,
  useMarkAsRead,
  useMarkAllAsRead,
} from '../hooks/use-notifications';

function getNotificationIcon(type: string) {
  switch (type) {
    case 'incident':
      return <AlertTriangle className="w-5 h-5 text-error-500" />;
    case 'escalation':
      return <ArrowUp className="w-5 h-5 text-warning-500" />;
    case 'resolved':
      return <CheckCircle className="w-5 h-5 text-success-500" />;
    default:
      return <Bell className="w-5 h-5 text-brand-500" />;
  }
}

function formatTimestamp(dateStr: string): string {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(dateStr));
}

export function NotificationsPage() {
  const { data: notifications, isLoading, error } = useRecentNotifications();
  const { data: unreadData } = useUnreadCount();
  const markAsReadMutation = useMarkAsRead();
  const markAllAsReadMutation = useMarkAllAsRead();

  const [filter, setFilter] = useState<'all' | 'unread' | 'read'>('all');

  const unreadCount = unreadData?.count ?? 0;

  const filteredNotifications = useMemo(() => {
    if (!notifications) return [];
    return notifications.filter((n) => {
      if (filter === 'unread' && n.isRead) return false;
      if (filter === 'read' && !n.isRead) return false;
      return true;
    });
  }, [notifications, filter]);

  const stats = useMemo(
    () => ({
      total: (notifications ?? []).length,
      unread: unreadCount,
      read: (notifications ?? []).filter((n) => n.isRead).length,
    }),
    [notifications, unreadCount],
  );

  const handleMarkAsRead = (id: string) => {
    markAsReadMutation.mutate(id);
  };

  const handleMarkAllAsRead = () => {
    markAllAsReadMutation.mutate(undefined);
  };

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <Loader2 className="w-8 h-8 animate-spin text-brand-500 mx-auto mb-3" />
          <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>{t("notifications.loading")}</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <AlertCircle className="w-8 h-8 text-error-500 mx-auto mb-3" />
          <p style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '0.5rem' }}>{t("notifications.loadFailed")}</p>
          <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
            {error instanceof Error ? error.message : t("common.errorOccurred")}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6 max-w-5xl mx-auto">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 style={{ fontSize: '1.875rem', fontWeight: 600 }}>{t("notifications.title")}</h1>
          <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '0.25rem' }}>
            {t("notifications.subtitle")}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {unreadCount > 0 && (
            <Button
              variant="outline"
              onClick={handleMarkAllAsRead}
              disabled={markAllAsReadMutation.isPending}
              className="bg-input-background"
            >
              <CheckCheck className="w-4 h-4 mr-2" />
              {markAllAsReadMutation.isPending ? t("notifications.marking") : t("notifications.markAllRead")}
            </Button>
          )}
          <Link to="/profile/notifications">
            <Button variant="outline" className="bg-input-background">
              <Settings className="w-4 h-4 mr-2" />
              {t("common.settings")}
            </Button>
          </Link>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Card className="p-4 bg-brand-500/10 border-brand-500/20">
          <div className="flex items-center justify-between">
            <div>
              <p style={{ fontSize: '0.75rem', color: '#94A3B8', fontWeight: 600 }}>
                {t("notifications.total")}
              </p>
              <p style={{ fontSize: '1.5rem', fontWeight: 700, color: '#3E7BFA' }}>
                {stats.total}
              </p>
            </div>
            <Bell className="w-8 h-8 text-brand-500" />
          </div>
        </Card>
        <Card className="p-4 bg-warning-500/10 border-warning-500/20">
          <div className="flex items-center justify-between">
            <div>
              <p style={{ fontSize: '0.75rem', color: '#94A3B8', fontWeight: 600 }}>
                {t("notifications.unread")}
              </p>
              <p style={{ fontSize: '1.5rem', fontWeight: 700, color: '#FB923C' }}>
                {stats.unread}
              </p>
            </div>
            <AlertTriangle className="w-8 h-8 text-warning-500" />
          </div>
        </Card>
        <Card className="p-4 bg-success-500/10 border-success-500/20">
          <div className="flex items-center justify-between">
            <div>
              <p style={{ fontSize: '0.75rem', color: '#94A3B8', fontWeight: 600 }}>
                {t("notifications.read")}
              </p>
              <p style={{ fontSize: '1.5rem', fontWeight: 700, color: '#22C55E' }}>
                {stats.read}
              </p>
            </div>
            <CheckCircle className="w-8 h-8 text-success-500" />
          </div>
        </Card>
      </div>

      <Card className="p-4 bg-card/80 backdrop-blur-sm">
        <Tabs value={filter} onValueChange={(v) => setFilter(v as typeof filter)} className="flex-1">
          <TabsList className="grid w-full grid-cols-3">
            <TabsTrigger value="all">{t("common.all")}</TabsTrigger>
            <TabsTrigger value="unread">
              {t("notifications.unreadTab")} {unreadCount > 0 && `(${unreadCount})`}
            </TabsTrigger>
            <TabsTrigger value="read">{t("notifications.readTab")}</TabsTrigger>
          </TabsList>
        </Tabs>
      </Card>

      <div className="space-y-3">
        {filteredNotifications.length > 0 ? (
          filteredNotifications.map((notification) => (
            <Card
              key={notification.id}
              className={`p-4 transition-all hover:shadow-md ${!notification.isRead ? 'bg-brand-500/5 border-brand-500/20' : 'bg-card/80'
                }`}
            >
              <div className="flex items-start gap-4">
                <div
                  className={`flex-shrink-0 w-10 h-10 rounded-lg flex items-center justify-center ${!notification.isRead ? 'bg-brand-500/10' : 'bg-muted'
                    }`}
                >
                  {getNotificationIcon(notification.type)}
                </div>

                <div className="flex-1 min-w-0">
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 mb-1">
                        <h3
                          style={{
                            fontSize: '0.9375rem',
                            fontWeight: !notification.isRead ? 600 : 500,
                          }}
                          className="truncate"
                        >
                          {notification.title}
                        </h3>
                        {!notification.isRead && (
                          <Badge className="bg-brand-500 text-white text-xs">{t("notifications.new")}</Badge>
                        )}
                      </div>
                      {notification.message && (
                        <p
                          style={{ fontSize: '0.875rem', color: '#94A3B8' }}
                          className="line-clamp-2"
                        >
                          {notification.message}
                        </p>
                      )}
                      <div className="flex items-center gap-3 mt-2">
                        <span style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                          {formatTimestamp(notification.createdAt)}
                        </span>
                        {notification.timeAgo && (
                          <span style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                            ({notification.timeAgo})
                          </span>
                        )}
                      </div>
                    </div>

                    <div className="flex items-center gap-1">
                      {!notification.isRead && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleMarkAsRead(notification.id)}
                          title={t("notifications.markAsReadTitle")}
                        >
                          <Check className="w-4 h-4" />
                        </Button>
                      )}
                      {notification.actionUrl && (
                        <Link to={notification.actionUrl}>
                          <Button variant="ghost" size="sm" title={t("notifications.viewDetailsTitle")}>
                            <ExternalLink className="w-4 h-4" />
                          </Button>
                        </Link>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </Card>
          ))
        ) : (
          <Card className="p-12 bg-card/80 backdrop-blur-sm">
            <div className="flex flex-col items-center justify-center text-center">
              <Bell className="w-16 h-16 text-muted-foreground mb-4" />
              <h3 style={{ fontSize: '1.125rem', fontWeight: 600 }}>
                {t("notifications.noNotifications")}
              </h3>
              <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '0.5rem' }}>
                {filter === 'unread'
                  ? t("notifications.allCaughtUp")
                  : t("notifications.adjustFilters")}
              </p>
            </div>
          </Card>
        )}
      </div>
    </div>
  );
}