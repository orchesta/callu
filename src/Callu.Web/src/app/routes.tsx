import { createBrowserRouter, redirect } from "react-router";
import { lazy, Suspense } from "react";
import { RouteLoadingFallback } from "@/shared/components/loading-fallbacks";
import { RouteErrorBoundary } from "@/shared/components/error-boundary";
import { RootLayout } from "./layouts/root-layout";

import { Login } from "@/features/auth/components/login";
import { ForgotPassword } from "@/features/auth/components/forgot-password";
import { ResetPassword } from "@/features/auth/components/reset-password";
import { InitialSetup } from "@/features/auth/components/initial-setup";
import { Dashboard } from "@/features/dashboard/components/dashboard-container";

import { AcceptInvitation } from "@/features/auth/components/accept-invitation";
import { AccessDenied } from "@/features/auth/components/access-denied";

const IncidentsList = lazy(() => import("@/features/incidents/components/list").then(m => ({ default: m.IncidentsList })));
const IncidentDetail = lazy(() => import("@/features/incidents/components/detail").then(m => ({ default: m.IncidentDetail })));
const ProfilePage = lazy(() => import("@/features/profile/components/index").then(m => ({ default: m.ProfilePage })));
const UsersPage = lazy(() => import("@/features/users/components/index").then(m => ({ default: m.UsersPage })));
const ConferencePage = lazy(() => import("@/features/conference/components/index").then(m => ({ default: m.ConferencePage })));
const ConferencesList = lazy(() => import("@/features/conferences").then(m => ({ default: m.ConferenceList })));
const ServicesList = lazy(() => import("@/features/services/components/list").then(m => ({ default: m.ServicesList })));
const ServiceDetail = lazy(() => import("@/features/services/components/detail").then(m => ({ default: m.ServiceDetail })));
const WebhookTemplateEditor = lazy(() => import("@/features/services/components/webhook-template-editor").then(m => ({ default: m.WebhookTemplateEditor })));
const WebhookCaptures = lazy(() => import("@/features/services/components/webhook-captures").then(m => ({ default: m.WebhookCaptures })));
const EscalationList = lazy(() => import("@/features/escalations/components/list").then(m => ({ default: m.EscalationList })));
const EscalationDetail = lazy(() => import("@/features/escalations/components/detail").then(m => ({ default: m.EscalationDetail })));
const SchedulesList = lazy(() => import("@/features/schedules/components/list").then(m => ({ default: m.SchedulesList })));
const ScheduleDetail = lazy(() => import("@/features/schedules/components/detail").then(m => ({ default: m.ScheduleDetail })));
const TeamsList = lazy(() => import("@/features/teams/components/list").then(m => ({ default: m.TeamsList })));
const TeamDetail = lazy(() => import("@/features/teams/components/detail").then(m => ({ default: m.TeamDetail })));
const CallLogsList = lazy(() => import("@/features/call-logs/components/list").then(m => ({ default: m.CallLogsList })));
const AuditLogList = lazy(() => import("@/features/audit-logs/components/list").then(m => ({ default: m.AuditLogList })));
const SettingsHub = lazy(() => import("@/features/settings/components/index").then(m => ({ default: m.SettingsHub })));
const CommunicationsHub = lazy(() => import("@/features/communications/components/index").then(m => ({ default: m.CommunicationsHub })));
const VoximplantManagement = lazy(() => import("@/features/voximplant/components/index").then(m => ({ default: m.VoximplantManagement })));
const NotificationsPage = lazy(() => import("@/features/notifications/components/notifications-page").then(m => ({ default: m.NotificationsPage })));
const EmailTemplates = lazy(() => import("@/features/settings/components/email-templates").then(m => ({ default: m.EmailTemplates })));
const StatusPageManagement = lazy(() => import("@/features/status-page/components/index").then(m => ({ default: m.StatusPageManagement })));
const PublicStatusPage = lazy(() => import("@/features/status-page/components/index").then(m => ({ default: m.PublicStatusPage })));
const SubscriptionConfirm = lazy(() => import("@/features/status-page/components/subscription-confirm").then(m => ({ default: m.SubscriptionConfirm })));
const SubscriptionUnsubscribe = lazy(() => import("@/features/status-page/components/subscription-confirm").then(m => ({ default: m.SubscriptionUnsubscribe })));
const TermsOfService = lazy(() => import("@/features/legal/components/legal-pages").then(m => ({ default: m.TermsOfService })));
const PrivacyPolicy = lazy(() => import("@/features/legal/components/legal-pages").then(m => ({ default: m.PrivacyPolicy })));
const ReportsPage = lazy(() => import("@/features/reports/components/index").then(m => ({ default: m.ReportsPage })));
const PostmortemsList = lazy(() => import("@/features/postmortems/components/index").then(m => ({ default: m.PostmortemsList })));
const RunbooksList = lazy(() => import("@/features/runbooks/components/index").then(m => ({ default: m.RunbooksList })));
const MaintenancePage = lazy(() => import("@/features/maintenance/components/index").then(m => ({ default: m.MaintenancePage })));
const SetupWizard = lazy(() => import("@/features/setup/components/index").then(m => ({ default: m.SetupWizard })));
const NotFoundPage = lazy(() => import("./pages/not-found").then(m => ({ default: m.NotFound })));

/**
 * Wrapper component for lazy-loaded routes with Suspense boundary
 */
function LazyRoute({ Component }: { Component: React.LazyExoticComponent<React.ComponentType> }) {
  return (
    <RouteErrorBoundary>
      <Suspense fallback={<RouteLoadingFallback />}>
        <Component />
      </Suspense>
    </RouteErrorBoundary>
  );
}

export const router = createBrowserRouter([
  {
    path: "/status/:slug",
    Component: () => <LazyRoute Component={PublicStatusPage} />,
  },
  {
    path: "/status",
    Component: () => <LazyRoute Component={PublicStatusPage} />,
  },
  {
    path: "/status/subscribe-confirm",
    Component: () => <LazyRoute Component={SubscriptionConfirm} />,
  },
  {
    path: "/status/unsubscribe",
    Component: () => <LazyRoute Component={SubscriptionUnsubscribe} />,
  },
  {
    path: "/legal/terms",
    Component: () => <LazyRoute Component={TermsOfService} />,
  },
  {
    path: "/legal/privacy",
    Component: () => <LazyRoute Component={PrivacyPolicy} />,
  },
  {
    path: "/conference/:token",
    Component: () => <LazyRoute Component={ConferencePage} />,
  },
  {
    path: "/login",
    Component: Login,
  },
  {
    path: "/auth/forgot-password",
    Component: ForgotPassword,
  },
  {
    path: "/auth/reset-password",
    Component: ResetPassword,
  },
  {
    path: "/auth/initial-setup",
    Component: InitialSetup,
  },
  {
    path: "/auth/accept-invitation",
    Component: AcceptInvitation,
  },
  {
    path: "/setup",
    Component: () => <LazyRoute Component={SetupWizard} />,
  },
  {
    path: "/auth/access-denied",
    Component: AccessDenied,
  },
  {
    path: "/",
    Component: RootLayout,
    children: [
      {
        index: true,
        loader: () => redirect('/dashboard'),
      },
      {
        path: "dashboard",
        Component: Dashboard,
      },
      {
        path: "notifications",
        Component: () => <LazyRoute Component={NotificationsPage} />,
      },
      {
        path: "incidents",
        Component: () => <LazyRoute Component={IncidentsList} />,
      },
      {
        path: "incidents/new",
        loader: () => redirect('/incidents'),
      },
      {
        path: "incidents/:id",
        Component: () => <LazyRoute Component={IncidentDetail} />,
      },
      {
        path: "conferences",
        Component: () => <LazyRoute Component={ConferencesList} />,
      },
      {
        path: "profile",
        Component: () => <LazyRoute Component={ProfilePage} />,
      },
      {
        path: "users",
        Component: () => <LazyRoute Component={UsersPage} />,
      },
      {
        path: "services",
        Component: () => <LazyRoute Component={ServicesList} />,
      },
      {
        path: "services/:id",
        Component: () => <LazyRoute Component={ServiceDetail} />,
      },
      {
        path: "services/:id/template",
        Component: () => <LazyRoute Component={WebhookTemplateEditor} />,
      },
      {
        path: "services/:id/captures",
        Component: () => <LazyRoute Component={WebhookCaptures} />,
      },
      {
        path: "escalations",
        Component: () => <LazyRoute Component={EscalationList} />,
      },
      {
        path: "escalations/:id",
        Component: () => <LazyRoute Component={EscalationDetail} />,
      },
      {
        path: "schedules",
        Component: () => <LazyRoute Component={SchedulesList} />,
      },
      {
        path: "schedules/:id",
        Component: () => <LazyRoute Component={ScheduleDetail} />,
      },
      {
        path: "teams",
        Component: () => <LazyRoute Component={TeamsList} />,
      },
      {
        path: "teams/:id",
        Component: () => <LazyRoute Component={TeamDetail} />,
      },
      {
        path: "call-logs",
        Component: () => <LazyRoute Component={CallLogsList} />,
      },
      {
        path: "audit-logs",
        Component: () => <LazyRoute Component={AuditLogList} />,
      },
      {
        path: "reports",
        Component: () => <LazyRoute Component={ReportsPage} />,
      },
      {
        path: "postmortems",
        Component: () => <LazyRoute Component={PostmortemsList} />,
      },
      {
        path: "runbooks",
        Component: () => <LazyRoute Component={RunbooksList} />,
      },
      {
        path: "maintenance",
        Component: () => <LazyRoute Component={MaintenancePage} />,
      },
      {
        path: "settings",
        Component: () => <LazyRoute Component={SettingsHub} />,
      },
      {
        path: "settings/communications",
        Component: () => <LazyRoute Component={CommunicationsHub} />,
      },
      {
        path: "settings/communications/voximplant/:id",
        Component: () => <LazyRoute Component={VoximplantManagement} />,
      },
      {
        path: "settings/email-templates",
        Component: () => <LazyRoute Component={EmailTemplates} />,
      },
      {
        path: "settings/status-page",
        Component: () => <LazyRoute Component={StatusPageManagement} />,
      },
      {
        path: "*",
        Component: () => <LazyRoute Component={NotFoundPage} />,
      },
    ],
  },
]);
