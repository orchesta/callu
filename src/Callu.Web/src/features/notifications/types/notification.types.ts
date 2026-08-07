/**
 * Notification Types — mirrors BE NotificationItemDto
 */

export interface NotificationItemDto {
  id: string;
  title: string;
  message?: string;
  /** Display type: "incident", "escalation", "resolved", "info" */
  type: string;
  actionUrl?: string;
  isRead: boolean;
  createdAt: string;
  /** Human-readable relative time (e.g., "2 min ago") */
  timeAgo: string;
}
