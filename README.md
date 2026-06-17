# Callu

Self-hosted incident management and on-call scheduling. Single-tenant: one deployment per organization, you own the data.

Stack: .NET 10, React 19, PostgreSQL, with optional Redis and RabbitMQ.

## Features

### Incident management
- Six-state lifecycle — Open → Acknowledged → Investigating → Mitigated → Resolved → Closed — with guarded transitions. Resolved/closed incidents can be reopened. Severity is Low/Medium/High/Critical.
- Per-incident append-only timeline, internal/pinned notes, manual escalate, reassign, and bulk acknowledge/resolve.
- Duplicate suppression: a second active incident sharing the same external alert id is rejected.
- Alert automation rules — priority-ordered rules match incident fields (severity, status, title, description, service, team) and run actions: auto-escalate, assign team or user, set severity, add a note, or suppress notifications. Rules evaluate against an incident only after it is created (not on update, and not against raw webhook payloads), so a status condition effectively matches new incidents only.
- Postmortems per incident with a Draft → In review → Published → Locked workflow and follow-up action items.
- Runbooks (Markdown) optionally linked to a service, with tags and usage tracking.

### Inbound integrations (webhooks)
- Per-service webhook endpoint authenticated by an API key, with optional HMAC-SHA256 signature verification. Bodies are capped and the endpoint is rate-limited per IP.
- Listening/capture mode records incoming requests (sensitive headers redacted, body trimmed) for inspection instead of creating incidents; a capture can be promoted into a template.
- JSONPath payload templates map external alerts to incident fields and open/resolve events. Built-in templates ship for Prometheus Alertmanager, Grafana Alerting, and generic JSON.
- Outbound acknowledgement webhooks per service, delivered with retries.

### On-call scheduling & escalation
- Timezone-aware schedules using NodaTime. Rotations are wall-clock templates in the schedule's IANA zone, pre-expanded into concrete UTC occurrence rows (30-day horizon) so on-call lookups are simple range scans — no recurrence math at query time.
- Recurrence: none, daily, weekly, biweekly, monthly, or a custom day interval, with an optional end date.
- Explicit DST policy: spring-forward gaps shift to the next valid instant; fall-back ambiguities take the earlier mapping. Shifts are wall-clock, so transition days span 23 or 25 hours of real time.
- Manual overrides stored as absolute UTC instants, so "cover for me from 17:00 Friday my time" resolves unambiguously.
- Escalation policies with ordered steps; each step targets an explicit user list, a schedule (page the on-call), or a team (whole team or on-call only). A background process advances unacknowledged incidents step by step — committing each advance before paging, enforcing a minimum inter-step delay, and recording an event when a step reaches nobody or the policy is exhausted.

### Notifications
- Per-user delivery over email, SMS, voice call, and in-app push (SignalR), governed by per-user preferences. Email and push default on; SMS and voice are opt-in.
- Timezone-aware quiet hours, bypassed for escalation pages. Failed deliveries retry with exponential backoff and are de-duplicated.
- Organization notification channels — Slack, Microsoft Teams, a custom webhook, or a shared email address — fire on incident created/acknowledged/resolved, filtered by minimum severity and an optional service allowlist, with their own retry ladder.

### Voice & video
- Voice calls and WebRTC video rooms through [Voximplant](https://voximplant.com/). VoxEngine scenarios are provisioned into your account automatically when credentials are configured.
- Per-incident conference rooms with per-participant join links (reusable for re-join; each yields a one-time Voximplant Web SDK login key, so no SIP password reaches the browser), a participant cap, and auto-expiry. When an SMS provider is configured, team members with a phone number are texted a join link.
- Per-language text-to-speech templates (English and Turkish built in) for call prompts. SIP trunk management with credentials encrypted at rest. Call logs per incident.

### Service catalog & status
- Service catalog with a dependency graph. Status changes cascade to dependents by criticality (worsenings only; recovery is cleared manually) and can optionally open incidents.
- HTTP health checks per status-page component, with a consecutive-failure threshold before a component flips status. Probe URLs are SSRF-checked (resolve to public IPs only, no redirects).
- Public status pages with components, operator-posted incidents and updates, day-by-day uptime, and double-opt-in email subscribers. Public responses strip internal probe configuration.
- Maintenance windows over an absolute UTC interval, in two modes: suppress incoming alerts, or auto-acknowledge them.

### Reporting
- Dashboard summary (status and severity counts, MTTA/MTTR, recent incidents) with incident-trend, MTTA/MTTR, and worst-uptime widgets.
- Reports: incident trends, MTTA/MTTR over time, per-service uptime, team performance, and severity distribution.

### Identity & access
- First-run wizard creates the initial administrator; the endpoint self-disables once an admin exists.
- Invitation-only onboarding — there is no open signup. Admins invite users by email; invitees set their own password via an emailed link.
- Claim-based RBAC with four roles (Admin, TeamLead, Member, Viewer). Team membership also records a Lead/Member/Observer label per member — descriptive today; it does not affect authorization or who gets paged (paging a team notifies every member).
- JWT access tokens with rotating HttpOnly refresh cookies and family-based reuse detection, plus per-token revocation and a security-stamp check — so role changes, user removal, and password resets invalidate live sessions immediately.
- Password policy, account lockout, and per-IP rate limiting on auth (and other sensitive) endpoints. An audit log records key domain mutations (incident state changes, service and status-cascade changes, escalation exhaustion, runbook and postmortem edits); it does not yet cover authentication events or user/role/team/settings administration, and is queryable via the API only (no dedicated UI).

### Operations
- Single Docker Compose stack. Database migrations run automatically on startup, guarded by a Postgres advisory lock so the API and Worker don't race.
- API/Worker split for background jobs; Redis and RabbitMQ are optional. OpenTelemetry traces export to the bundled Jaeger (metrics are also emitted over OTLP but need a metrics-capable collector to view — Jaeger stores traces only).
- Provider, SMTP, and SIP secrets are encrypted at rest with ASP.NET Data Protection. SMTP and communication providers are configured in-app after first login, not through environment variables.
- English and Turkish UI translations. The public status page has a language switcher; the authenticated app currently selects locale from a stored preference (an in-app toggle is on the roadmap).

## Quick start

Requires [Docker](https://www.docker.com/) and Docker Compose.

```bash
git clone https://github.com/orchesta/callu.git
cd callu
cp .env.example .env
```

Edit `.env` and set at least `POSTGRES_PASSWORD`, `JWT_SECRET_KEY`, and `RABBITMQ_PASS`. `JWT_SECRET_KEY` must be at least 32 bytes or the API refuses to start. A minimal `.env` is just:

```dotenv
POSTGRES_PASSWORD=a_long_random_db_password
RABBITMQ_PASS=a_long_random_rabbitmq_password
# at least 32 characters — generate one with:  openssl rand -base64 48
JWT_SECRET_KEY=a_long_random_signing_key_min_32_chars
```

Everything else has sensible defaults (see [`.env.example`](.env.example) for the full set). Then:

```bash
docker compose up -d
```

Open `http://localhost:3000`. The first visit routes to the setup wizard to create the administrator account. After logging in, configure SMTP (Settings → Email) and your voice/SMS providers (Settings → Communications) — these live in the database, not in `.env`.

The stack is seven services: PostgreSQL, Redis, RabbitMQ, Jaeger (internal OTLP), the .NET API, the worker, and the React SPA behind nginx.

**Ports.** Only `3000` (web) is published to the host. Jaeger UI is bound to loopback at `http://127.0.0.1:16686`. The RabbitMQ management UI is at `http://localhost:15672` (credentials from `RABBITMQ_USER` / `RABBITMQ_PASS`). OTLP gRPC (4317) stays on the internal network.

**Images.** `docker-compose.yml` pulls published images (`orchestalabs/callu-api`, `callu-worker`, `callu-web`) at a pinned tag. [`docker-compose.override.yml`](docker-compose.override.yml) adds `build:` blocks and is auto-merged by Docker Compose when you build from source locally.

**Telemetry.** Set `OPEN_TELEMETRY_ENABLED=false` in `.env` to disable export. For local `dotnet run` without Docker, either set `OTEL_EXPORTER_OTLP_ENDPOINT` or set `OpenTelemetry:OtlpEndpoint` in config.

**Worker vs API.** By default the API container has `Callu__EnableBackgroundServices=false` and the worker runs the periodic jobs (escalation dispatch, notification retries, schedule materialization, health checks, conference/token cleanup). New incidents publish `TriggerIncidentEscalation` to RabbitMQ when `RabbitMQ:Host` is set; when empty, escalation runs in-process on the API via `DirectEscalationWorkflowSignal`.

## Tech stack

**Backend:** .NET 10, ASP.NET Core, EF Core, PostgreSQL, ASP.NET Identity, JWT. NodaTime for timezone math. Redis + RabbitMQ are optional. MassTransit provides an EF transactional outbox on the API and a transactional consumer inbox on the worker. SignalR for real-time push. Quartz.NET for worker scheduling. ASP.NET Data Protection for secret-at-rest encryption. Serilog for logging, OpenTelemetry for traces/metrics.

**Frontend:** React 19, TypeScript, Vite, Tailwind CSS 4, Radix UI, TanStack Query.

## Architecture

Clean Architecture, four backend layers plus a shared DTO library and the SPA:

```
src/
  Callu.Domain/          Entities, enums, base types
  Callu.Application/     Service + repository contracts, messaging contracts
  Callu.Infrastructure/  EF Core, Identity, MassTransit, Quartz, providers
  Callu.Shared/          DTOs, Result<T>, localization
  Callu.Api/             ASP.NET Core host (controllers, middleware, SignalR hub)
  Callu.Worker/          Background host (Quartz jobs, MassTransit consumers)
  Callu.Web/             React SPA (feature-based layout)
```

Both hosts share `Callu.Infrastructure`. They're wired for different responsibilities via `CalluMessagingHostRole` (`ApiPublisher` vs `WorkerConsumer`) and `CalluTelemetryHostKind` (`Api` vs `Worker`). Both run EF migrations at startup under a shared advisory lock, and both must share the same Data Protection key ring (the `callu_dpkeys` volume, or Redis when configured) so secrets written by one host decrypt on the other.

### Backend conventions
- Controllers are thin; they use policy-based `[Authorize]` and return `Result<T>` / `Result`. An `ApiResponseWrapperFilter` wraps responses in `ApiResponse<T>`. FluentValidation runs via a filter.
- Repositories are generic `IRepository<T>` plus per-entity specializations.
- `HybridCache` is the caching abstraction. Setting `ConnectionStrings:Redis` enables L2 and the SignalR Redis backplane.
- Audit fields (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`) on every `BaseEntity` are populated automatically. Soft-delete is enforced through EF global query filters.

### Frontend conventions
Feature-sliced under [`src/Callu.Web/src/features/`](src/Callu.Web/src/features/); each feature has `api/`, `hooks/`, `types/`, `components/`, and an `index.ts` barrel. Cross-cutting code lives in [`src/shared/`](src/Callu.Web/src/shared/). API calls go through a typed `apiClient` that unwraps the `ApiResponse<T>` envelope. Data fetching uses `useApiQuery` / `useApiMutation` over TanStack Query with hierarchical keys.

## Local development

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js 20+](https://nodejs.org/), PostgreSQL 15+.

### Backend

Set your connection string in `src/Callu.Api/appsettings.Development.json`, then:

```bash
cd src
dotnet restore
dotnet ef database update --project Callu.Infrastructure --startup-project Callu.Api
dotnet run --project Callu.Api
```

The API listens on `http://localhost:5095`.

To run the worker in a separate process (matches the Docker topology), start it after Redis is up if you use `ConnectionStrings:Redis`:

```bash
dotnet run --project Callu.Worker
```

Note that `Callu.Api/appsettings.json` ships `EnableBackgroundServices: false`, so a standalone API runs **no** periodic jobs — set it to `true` for an API-only local setup. When both API and Worker run, keep it `false` on the API so the timers don't fire in both places. Some jobs (voice/webhook/notification-channel retries, conference-room expiry, refresh-token cleanup) exist only on the Worker, so the Worker is required for full operational coverage.

Callu.Worker schedules periodic work with [Quartz.NET](https://www.quartz-scheduler.net/): escalation dispatch (every 10s), notification retries (10s), health checks (15s), schedule materialization (daily 03:00 UTC), voice-call retries, conference-room expiry, webhook-delivery retries, refresh-token cleanup, and notification-channel retries. The default job store is in-memory (`Quartz:UsePersistentStore = false`). For clustered workers, set it to `true` and create the `qrtz_*` tables from the upstream [tables_postgres.sql](https://github.com/quartznet/quartznet/blob/main/database/tables/tables_postgres.sql).

### Frontend

```bash
cd src/Callu.Web
npm install
npm run dev
```

Runs at `http://localhost:5173`.

## Configuration

In Docker, settings come from environment variables (double-underscore form, e.g. `ConnectionStrings__Redis`). [`.env.example`](.env.example) is the full reference; the table below lists the settings that matter most. For local `dotnet run`, the same keys live in `appsettings.json` in colon form.

**Backend:**

| Setting | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `ConnectionStrings:Redis` | Optional Redis. Enables HybridCache L2, the SignalR backplane, and the Data Protection key ring. When empty, cache stays in-process, SignalR is single-host, and keys go to the local filesystem |
| `RabbitMQ:Host` / `Username` / `Password` / `VirtualHost` | Optional broker. With `Host` set, the API publishes via EF bus outbox and the Worker consumes via EF consumer inbox (`InboxState`, `OutboxState`, `OutboxMessage` tables). Empty host keeps escalation in-process |
| `Callu:EnableBackgroundServices` | API-side toggle for in-process timers. Set `false` when Callu.Worker runs the jobs |
| `Quartz:UsePersistentStore` | Worker only. `false` (default) = in-memory; `true` = clustered ADO job store on `DefaultConnection` (`qrtz_*` tables) |
| `JwtSettings:SecretKey` / `Issuer` / `Audience` | Signing key (≥ 32 bytes, required) plus issuer/audience. The API refuses to start if these are missing or too short |
| `JwtSettings:AccessTokenExpirationMinutes` / `RefreshTokenExpirationDays` | Token lifetimes (defaults 15 min / 7 days) |
| `Cors:AllowedOrigins` | Browser origins allowed to call the API with credentials |
| `CalluSettings:ApiUrl` | Public base URL used to build links in invitation, reset, and conference-invite emails |
| `OpenTelemetry:Enabled` / `OtlpEndpoint` | Master switch and OTLP gRPC collector endpoint |
| `ForwardedHeaders:KnownNetworks` / `KnownProxies` | Trusted reverse-proxy CIDRs/IPs behind nginx |
| `Serilog:MinimumLevel` | Log level (default Warning) |

SMTP credentials and voice/SMS provider configuration are **not** environment settings — they are stored in the database and edited in the Settings UI (passwords encrypted at rest).

**Frontend** (`.env`):

| Variable | Purpose |
|---|---|
| `VITE_API_URL` | Backend API base URL |
| `VITE_AUTH_TOKEN_KEY` | LocalStorage key for the auth token |
| `VITE_API_TIMEOUT` | Request timeout in milliseconds |

## API

All endpoints are versioned under `/api/v1/`. Authentication is JWT Bearer with HttpOnly refresh-cookie rotation. Each area is gated by claim-based policies; inbound webhooks and public status-page reads are anonymous.

| Area | Key endpoints |
|---|---|
| Setup | `GET /setup/status`, `POST /setup/initial` (first run only, self-disables) |
| Auth | `login`, `refresh`, `logout`, `me`, `accept-invitation`, `forgot-password`, `reset-password` |
| Users | list, get, update, `invite`, `{id}/role`, `{id}/resend-invitation`, soft delete |
| Teams | CRUD, members add/remove, member-role updates |
| Profile | get/update, `change-password`, `notification-preferences` |
| Incidents | CRUD, `acknowledge`/`resolve`/`close`/`reopen`/`escalate`, `assign`, `timeline`, `conference`, `webhook-deliveries`, `bulk/acknowledge`, `bulk/resolve`, notes CRUD |
| Alert rules | CRUD, `{id}/toggle`, `metadata` (condition fields, operators, action types) |
| Postmortems | CRUD, `by-incident/{id}`, `submit`/`reject`/`publish`/`lock` |
| Runbooks | CRUD, `by-service/{id}`, `mark-used` |
| Escalations | policies CRUD, steps CRUD, step reorder |
| Schedules | CRUD, `on-call`, `occurrences`, rotations CRUD, overrides CRUD |
| Services | CRUD, dependencies, webhook settings, captures, webhook templates |
| Webhooks (inbound) | `POST /webhooks/{token}?apiKey=` (anonymous, rate-limited) |
| Status pages | admin CRUD + components + incidents/updates + stats + subscribers; anonymous `slug/{slug}`, `uptime`, `subscribe`, `confirm`, `unsubscribe` |
| Maintenance windows | list, `active`, create, cancel, delete |
| Notifications | in-app inbox: list, `unread-count`, `read`, `read-all`, delete |
| Notification channels | CRUD, `toggle`, `test`, `types`, `severity-options` (Slack/Teams/Webhook/Email) |
| Providers | communication providers CRUD + test, capability mappings, SIP trunks, TTS templates |
| Conferences | create/end room, `validate`/`join`/`leave` (anonymous join), admin list |
| Voximplant | management passthrough (account/applications/scenarios/rules/users), provision, sync-users |
| Call logs | list, per-incident |
| Dashboard | `summary`, `incident-counts`, `system-health` |
| Reports | `incident-trends`, `mtt-metrics`, `service-uptime`, `team-performance`, `severity-distribution` |
| Settings | `organization`, `smtp` (+ test), `email-templates` (standalone editor with preview/test — not yet wired into the live email pipeline, which uses built-in file templates), `localization/timezones` |
| Audit log | query by entity, paged |

Real-time updates are delivered over a SignalR hub at `/hubs/notifications` (per-user groups; incident, notification, and settings events).

## External services

Voice calls and video rooms use [Voximplant](https://voximplant.com/). VoxEngine scenarios (`callu-incident-call.js`, `callu-conference.js`) live in [`src/Callu.Infrastructure/Providers/Voximplant/Scripts/`](src/Callu.Infrastructure/Providers/Voximplant/Scripts/) and are provisioned into your account automatically when credentials are configured (a public, non-localhost base URL is required to provision).

Scenario callbacks carry a scenario API key, a timestamp, and a single-use nonce — the backend rejects requests outside a 5-minute window or with a replayed nonce (the nonce store is distributed when Redis is configured, so it holds across API replicas). Per-call data tokens are separately one-time-use and expire after 10 minutes. Management API calls use JWT Bearer when a Service Account JSON is configured; otherwise they fall back to the legacy `api_key` parameter.

SMS is provider-pluggable through a capability registry: [Verimor](https://www.verimor.com.tr/) and a generic HTTP SMS gateway (operator-defined request template) ship out of the box, and the active provider is chosen in the Communications settings rather than hardcoded.

## License

[MIT](LICENSE)
