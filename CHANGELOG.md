# Changelog

Notable, user-facing changes to Callu. This project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

- **Applications**: inbound webhooks and webhook templates moved from Settings to their
  own page. An application endpoint can now exist without a service — it then captures
  every alarm it receives instead of rejecting them — and can be bound to a service
  (or a newly created one) later, from the captures screen. Team leads can manage the
  page; provider setup stays admin-only.
- Listening mode is now available on application endpoints, and an endpoint whose
  service was deleted captures instead of silently answering 400.
- Templates built from a captured request now attach to the endpoint the capture
  arrived through, and unsaved template mappings can be tried against a sample payload
  (`POST /api/v1/webhook-templates/preview`).
- **Captured requests are now capped at the newest 500 per endpoint — this includes
  existing per-service captures, which were previously unlimited.** Older rows beyond
  the cap are deleted as new requests arrive. Export anything you need to keep before
  upgrading if you rely on an unbounded capture history.
- The webhook rate limit default rose from 100 to 1000 requests/min per IP and became
  configurable via `Callu:WebhookRateLimit` (`PermitLimit`, `WindowSeconds`, `QueueLimit`).
- Incident descriptions between 2000 and 4000 characters no longer get rejected on
  ingest; the validator now matches the column bound, so those alerts create incidents
  instead of being dropped.
- One additive migration (`AddIntegrationListeningCaptures`) applies automatically on
  startup: two new columns and one NOT NULL relaxation, nothing destructive.

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
