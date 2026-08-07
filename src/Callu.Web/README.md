# Callu Web

The React SPA for Callu. In the Docker stack it is built into static files and served
by nginx alongside the API; for development it runs against a local API on port 5095.

Node 22 is what CI builds with.

## Quick start

```bash
cp .env.example .env.local     # then set VITE_API_URL if your API is not on :5095
npm install
npm run dev                    # http://localhost:3000
```

The API must be running separately (`dotnet run --project ../Callu.Api`), and so must
the Worker if you want escalation, retries or scheduled jobs to happen.

## Scripts

| Script | What it does |
|---|---|
| `npm run dev` | Vite dev server on port 3000 |
| `npm run build` | Production build into `dist/` |
| `npm run preview` | Serve the production build locally |
| `npm run type-check` | `tsc --noEmit` |
| `npm run lint` | ESLint; fails on any warning |
| `npm run lint:fix` | ESLint with `--fix` |
| `npm run test` | Vitest in watch mode |
| `npm run test:ci` | Vitest, single run |
| `npm run test:coverage` | Coverage report |
| `npm run analyze` | Bundle visualizer |

`type-check`, `lint` and `test:ci` all have to pass before a PR; the coverage
thresholds in `vitest.config.ts` are a floor that may be raised but not lowered.

## Configuration

Three environment variables, all optional — each falls back to the default below.
The Docker image is built without any of them, in which case the SPA calls its own
origin, which is what nginx serves.

| Variable | Default |
|---|---|
| `VITE_API_URL` | `window.location.origin` |
| `VITE_AUTH_TOKEN_KEY` | `calluapp_auth_token` |
| `VITE_API_TIMEOUT` | `30000` (ms) |

They are read in one place, `src/shared/config.ts`.

## Layout

Feature-sliced: each feature owns its `api/`, `hooks/`, `types/`, `components/` and an
`index.ts` barrel. Anything cross-cutting lives in `shared/`.

```
src/
├── app/            Shell, routes, route guards
├── shared/         API client, auth, SignalR, UI components, hooks, validations
├── features/       One directory per area: audit-logs, auth, call-logs,
│                   communications, conference(s), dashboard, diagnostics,
│                   escalations, incidents, legal, maintenance, notifications,
│                   postmortems, profile, reports, runbooks, schedules, services,
│                   settings, setup, status-page, teams, users, voximplant
├── styles/         Global styles and theme
└── test/           Test setup and helpers
```

## Conventions

- All HTTP goes through the typed `apiClient` in `shared/api/`, which unwraps the
  backend's `ApiResponse<T>` envelope.
- Data fetching uses the `useApiQuery` / `useApiMutation` wrappers over TanStack
  Query, with hierarchical query keys. Every parameter that changes the response
  belongs in the key.
- Forms use react-hook-form with zod resolvers. Keep zod bounds at or below the
  backend's, so the operator gets a field error rather than a bare 400.
- Real-time updates come over SignalR from `/hubs/notifications`.
- On-call schedules are timezone-aware: handover is decided by the schedule's IANA
  zone, and overrides are absolute UTC instants. Do not do wall-clock arithmetic in
  the browser's zone.

## Stack

React 19, TypeScript, Vite 6, Tailwind CSS v4, TanStack Query v5, React Router v7,
react-hook-form + zod, Radix UI primitives, lucide-react, Recharts, cmdk, SignalR,
Vitest + Testing Library.

## License

MIT — see [LICENSE](../../LICENSE) at the repository root.
