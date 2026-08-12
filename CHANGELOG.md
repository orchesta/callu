# Changelog

Notable, user-facing changes to Callu. This project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 1.2.0 — 2026-08-12

- **Service actions**: a service can now carry operator-defined outbound HTTP actions
  ("Restart Redis"-style buttons) that a responder runs from the incident page, under
  the same team scope as every other incident operation. Each run is sent exactly
  once — never retried — behind a confirm dialog, a server-side double-click guard
  backed by a unique in-flight index (409), and a new `CanExecuteServiceActions`
  permission seeded to Admin, TeamLead and Member. Every run is recorded in the
  delivery ledger, with timeline and audit entries written alongside it best-effort;
  the synchronous result (HTTP status, clipped response) is shown to the responder.
  Configured on the service's Actions tab (`CanManageServices`), at most 50 per
  service; URLs and payload templates are validated at save time, and custom header
  values are returned only to service managers.
- **The ACK callback can now fire on more lifecycle events**: created, acknowledged,
  resolved, closed and reopened, selectable per service. Existing configurations keep
  firing exactly as before (acknowledged + resolved) with no migration or operator step.
- **ACK callbacks now fire from every path that changes incident state**: maintenance-window
  auto-acknowledge, the generic incident update endpoint, reassignment that implicitly
  acknowledges, and phone acknowledgements (keypress or conference start) previously
  told the alert source nothing.
- The ACK callback can sign with a dedicated outbound secret (`AckSecret`); when unset,
  the previous behavior — signing with the inbound webhook secret — is unchanged.
- A broken ACK payload template and an exhausted retry chain are no longer silent: both
  write a terminal Failed delivery row, a timeline entry and an audit row. Payload
  templates and ACK URLs are validated when the configuration is saved, not first
  discovered at send time.
- ACK configuration can be set when creating a service, and every ACK field is now
  validated to its column bounds (over-long values get a 400 instead of a 500).
- **Partial service updates no longer erase omitted fields.** `PUT /api/v1/services/{id}`
  now treats an omitted or null field as "leave unchanged" (an empty string clears
  nullable text fields). Previously a partial body silently nulled the ACK configuration
  and flipped `isPublic`. The one deliberate exception: an omitted `teamId` still clears
  the team assignment, which is how the UI removes a team.

## 1.1.0 — 2026-08-11

- **Applications**: inbound webhooks and webhook templates moved from Settings to their
  own page. An application endpoint can now exist without a service — it then captures
  every alarm it receives instead of rejecting them — and can be bound to a service
  (or a newly created one) later, from the captures screen. Team leads can manage the
  page; provider setup stays admin-only.
- Listening mode is now available on application endpoints, and an endpoint whose
  service was deleted captures instead of silently answering 400. Binding an endpoint
  to a service turns listening off, so alarms start opening incidents right after the
  bind; unbinding turns it back on.
- Templates built from a captured request now attach to the endpoint the capture
  arrived through; a hand-written template can be attached to an endpoint in the same
  create call. Unsaved template mappings can be tried against a sample payload from
  the template editor (`POST /api/v1/webhook-templates/preview`).
- **Captured requests are now capped at the newest 500 per endpoint — this includes
  existing per-service captures, which were previously unlimited.** Older rows beyond
  the cap are deleted as new requests arrive. Export anything you need to keep before
  upgrading if you rely on an unbounded capture history.
- **Deleting captures (single or "Clear all") is now permanent** — previously the rows
  were only hidden and kept occupying disk. Capture lists are paginated (50 per page),
  so the whole retained history is reachable from the UI.
- Captures taken through a bound application endpoint no longer appear in — or get
  deleted by — the service's own captures page; each endpoint owns its history.
- A captured payload that was cut at the 64 KB storage limit now gets a clear
  "truncated" error in preview and template-from-capture instead of a confusing
  parse failure.
- Rotating an application endpoint's credentials is admin-only again; team leads keep
  full management of endpoints, captures and templates.
- The webhook rate limit default rose from 100 to 1000 requests/min per IP and became
  configurable via `Callu:WebhookRateLimit` (`PermitLimit`, `WindowSeconds`, `QueueLimit`).
- Incident descriptions between 2000 and 4000 characters no longer get rejected on
  ingest; the validator now matches the column bound, so those alerts create incidents
  instead of being dropped.
- **Fixed: a fresh 1.0.0 install could not create its first admin** — `POST /setup/initial`
  answered 500 because of a validation attribute MVC refuses on record properties. The same
  defect broke the public status-page subscribe endpoint and token refresh with a JSON body
  (browser sessions were unaffected — the SPA refreshes via the cookie alone). All three
  request records are fixed and a test now scans for the shape.
- Hardening from a full CodeQL pass: external input is sanitized before it reaches log
  lines, email addresses and phone numbers are masked in log output, silent JSON-parse
  fallbacks (alert-rule conditions, template severity/state mappings, locale files,
  health-check headers) now log and surface instead of defaulting quietly, and the
  SSRF address check fails closed one layer earlier.
- Two additive migrations (`AddIntegrationListeningCaptures`,
  `AddWebhookCaptureCompositeIndexes`) apply automatically on startup: two new columns,
  one NOT NULL relaxation and two index swaps, nothing destructive.

## 1.0.0 — 2026-08-07

First release. What ships in it, briefly — the [README](README.md) has the detail:

- Incident management: six-state lifecycle, append-only timeline, notes, postmortems
  and runbooks.
- Inbound webhooks per service (API key + optional HMAC), with templates for
  Prometheus Alertmanager, Grafana Alerting and generic JSON, plus a capture mode
  for unknown payloads.
- Timezone-aware on-call schedules (NodaTime, explicit DST policy), overrides, and
  ordered escalation policies targeting users, schedules or teams.
- Notifications over email, SMS, voice and in-app / mobile push, with per-user
  preferences, quiet hours, retries, and Slack / Teams / webhook / email
  organization channels.
- Voice paging through Voximplant or the self-hosted
  [callu-voice](https://github.com/orchesta/callu-voice) (Asterisk + local TTS, your
  SIP carrier is the only outside party), with per-channel provider routing, TTS
  templates in English and Turkish, and a browser preview of what a call will say.
- Per-incident WebRTC conference rooms.
- Service catalog with dependency cascade, HTTP health checks, public status pages
  and maintenance windows.
- Dashboard and reports: MTTA / MTTR, trends, uptime, team performance.
- Invitation-only onboarding, claim-based RBAC (five roles), JWT with rotating
  refresh tokens, and a tamper-evident audit trail with CSV / JSON Lines
  (OpenAuditModel-shaped) export, forwarding and archiving.
- One Docker Compose stack; migrations run automatically on startup.
