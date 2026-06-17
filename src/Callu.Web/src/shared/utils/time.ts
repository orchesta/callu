/**
 * Time utility functions used across the application.
 */

/**
 * Returns a human-readable relative time string.
 *
 * @example
 * getTimeAgo('2024-01-01T00:00:00Z') // "3 hours ago"
 */
export function getTimeAgo(date: Date | string): string {
  const seconds = Math.floor(
    (new Date().getTime() - new Date(date).getTime()) / 1000,
  );

  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
  return `${Math.floor(seconds / 86400)}d ago`;
}

/**
 * Format a date for display in detail views (locale-aware).
 */
export function formatDateTime(date: Date | string | null | undefined): string {
  if (!date) return '—';
  return new Date(date).toLocaleString();
}
