# Changelog

Notable, user-facing changes to Callu. This project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 1.2.1 — 2026-06-18

- Security maintenance: updated web dependencies (React Router, Vite, and a few
  transitive packages) to clear known vulnerability advisories. No functional changes.

## 1.2.0 — 2026-06-18

- Video conferencing now uses the Voximplant Web SDK v5. The connection node
  (NODE_1…NODE_12) is chosen in the provider settings instead of an environment variable.
- More reliable video conferences — fixes to sign-in, call routing, multi-party video,
  and rejoining after a page refresh.
- Admins can manage each user's notification channels (Email / SMS / Voice / Push) from
  User Management.
- Status-page subscriber emails are now sent manually from a "Notify subscribers" button
  (dispatched in the background) instead of automatically on every incident update.

## 1.0.0 — 2026-06-17

- First public release: a self-hosted, single-tenant incident-management and on-call
  platform — incidents, alert rules, escalation policies, on-call schedules, video
  conferences, status pages, and notifications — deployable via Docker Compose.
