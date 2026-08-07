# Changelog

Notable, user-facing changes to Callu. This project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
