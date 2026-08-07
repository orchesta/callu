# Contributing to Callu

Thanks for considering a contribution to Callu. It's a small, self-hosted project, and help is welcome.

## 💻 Local Development Setup

To get your local environment ready for coding:

1. **Prerequisites:**
   - [.NET 10 SDK](https://dotnet.microsoft.com/download)
   - [Node.js](https://nodejs.org/) (v20 or newer)
   - Docker & Docker Compose
2. **Setup PostgreSQL:**
   The `callu-db` service in `docker-compose.yml` does not publish a host port (the app containers reach it over the internal network), so for a host-side `dotnet run` start a database that does:
   ```bash
   docker run -d --name callu-db-dev -p 5432:5432 \
     -e POSTGRES_DB=calludb -e POSTGRES_USER=callu -e POSTGRES_PASSWORD=callu_dev_password \
     postgres:16.14-alpine
   ```
3. **Configure the API:**
   `appsettings.Development.json` and `Properties/launchSettings.json` are gitignored, so a fresh clone starts in the `Production` environment with no connection string. The committed `appsettings.json` is the template — copy it and fill the copy in:
   ```bash
   cp src/Callu.Api/appsettings.json src/Callu.Api/appsettings.Development.json
   ```
   Set `ConnectionStrings:DefaultConnection` and `JwtSettings:SecretKey` (at least 32 bytes — `openssl rand -base64 48`). The API refuses to start if the key is empty, shorter than that, or still a `CHANGE_ME`-style placeholder.
4. **Start the API (Backend):**
   ```bash
   export ASPNETCORE_ENVIRONMENT=Development
   export ASPNETCORE_URLS=http://localhost:5095
   dotnet run --project src/Callu.Api
   ```
   *Note: Our application automatically runs `dbContext.Database.MigrateAsync()` on startup, so it will create the tables for you.*

   > ⚠️ **Warning:**
   > Database migrations are executed automatically at application startup.
   > A faulty migration will prevent the application from starting and cause a crash loop.
   > Always test migrations against a database containing real-like data before submitting a PR.
5. **Start the Worker:**
   All scheduled jobs (escalation dispatch, notification retries, schedule materialization, health checks, cleanup, retention pruning) run on the Worker — the API schedules none of them. Run it alongside the API, passing the same connection string:
   ```bash
   export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=calludb;Username=callu;Password=callu_dev_password"
   dotnet run --project src/Callu.Worker
   ```
6. **Start the Web UI (Frontend):**
   ```bash
   cd src/Callu.Web
   cp .env.example .env
   npm install
   npm run dev
   ```
   The dev server listens on `http://localhost:3000` (`vite.config.ts`). All three `VITE_*` variables are optional and fall back to a default, but set `VITE_API_URL=http://localhost:5095` — unset, the SPA calls its own origin, which is correct behind nginx and wrong here.

---

## 🏛️ The "Golden Rules" of Database Migrations

Callu is distributed as a self-hosted Docker container, so an existing instance may upgrade straight to your code with live data in its database. **Because migrations run automatically at startup, a destructive change can break that upgrade.**

If your PR modifies the database (i.e., changing Entities in `Callu.Domain`), you **MUST** follow these strict rules to prevent data loss:

### ❌ Rule 1: NEVER Drop Columns or Tables
If you remove a property from an entity and create a migration, EF Core will generate a `DROP COLUMN` script. Users will permanently lose all data stored in that column.
> **Do This Instead (Expand & Contract Pattern):** 
> 
> **Recommended Migration Strategy: Expand → Migrate → Contract**
> 1. **Expand**: Add new columns/tables (nullable, backward-compatible)
> 2. **Migrate**: Move data using background jobs or SQL scripts
> 3. **Contract**: Remove old columns in a future major release
> 
> Add the new properties you need, but leave the old property intact. You can mark it as `[Obsolete]` in C# so other developers stop using it. Dropping columns requires a heavily orchestrated major release and deprecation warnings.

### ❌ Rule 2: NEVER Rename Columns
Renaming a property often causes EF Core to drop the old column and create a new empty one, destroying existing data. Even if EF Core generates a `RenameColumn` operation, it may not be safe across all environments. Treat renames as drop + add.
> **Do This Instead:** Create the new column as an "addition" (Expand phase). Keep writing to both or use SQL/Background logic to migrate the actual row data before deprecating the old column.

### ❌ Rule 3: NEVER Shrink Data Types
If you change `string (Max 1000)` to `string (Max 100)`, any row that already holds a longer value will fail the startup migration, leaving the API in a crash loop.
> **Do This Instead:** You can only safely expand sizes (e.g., 100 to 1000), never shrink. Always assume the user's database contains the maximum possible length.

### ❌ Rule 4: NEVER Add Non-Nullable Columns Without a Default Value
Adding a new non-nullable column to an existing table will fail if the table already contains data.

> **Do This Instead:** 
> Add the column as nullable first, populate the data via a background job or SQL script, and only then make it non-nullable in a later release.

### ⚠️ Rule 5: Migrations Must Be Version-Tolerant
Users may upgrade across multiple versions at once. Your migration must work regardless of which previous version the database is on.

### ✅ Rule 6: Generating Your Migration
Once your entity changes are completely backward-compatible, generate the migration from the repository root:
```bash
dotnet ef migrations add MyNewFeature --project src/Callu.Infrastructure --startup-project src/Callu.Api
```

---

## 🤝 Pull Request Process

1. **Branching:** Fork the repo and create your branch from `main`.
2. **Focus:** Ensure your PR does exactly one thing. Do not mix refactoring with feature delivery.
3. **Tone:** Callu maintains a "humble, non-exaggerated, and technically accurate" tone in its UI and Documentation. Avoid over-promising or using extreme marketing language ("World's fastest", "Incredible", etc.).
4. **Validation:** Before submitting, please verify:
   - `dotnet build` succeeds with zero errors **and zero warnings** — CI builds with `-warnaserror`, so a warning fails the PR. XML doc comments are part of that: `GenerateDocumentationFile` is on, which means a malformed `<summary>` or a `///` orphaned from its member is a warning.
   - `dotnet test` passes (the solution includes a `Callu.Tests` project). Run it with Docker available: the migration, concurrency and reaper tests use Testcontainers and are **skipped** without it, so a green run on a machine with Docker off has not exercised them. `CALLU_REQUIRE_DOCKER=1` turns that skip into a failure — CI sets it.
   - Inside `Callu.Web`, run `npm run type-check`, `npm run lint`, `npm run build`, and `npm run test:ci` — all must pass. `vite build` transpiles via esbuild and strips types without failing on TypeScript type errors, so `npm run type-check` (`tsc --noEmit`) and `npm run lint` (`eslint --max-warnings 0`) are required to catch type and lint issues. Use `test:ci` rather than `test`, which starts vitest in watch mode.

   CI (`.github/workflows/ci.yml`) runs all of the above on every push and pull request to `main` — with `-warnaserror` on the backend build and `npm run test:coverage:ci` in place of `test:ci`, so a new warning or a drop in frontend coverage also fails the build. On top of that it runs a vulnerability audit that fails on any High or Critical advisory in the restored package graph (`dotnet list package --vulnerable --include-transitive`; the same warnings appear on the build but are deliberately not fatal there, so a newly-published advisory does not fail every open PR's build step), `dotnet ef migrations has-pending-model-changes`, which catches an entity edit with no matching migration, and a migration-safety job that reads the `Up()` body of every added or modified migration: it fails Rules 1–4 above (drop/rename, a narrowed column, a `NOT NULL` column with no default) and, because it cannot reason about raw SQL, requires a `// migration-safety: reviewed <why>` marker on any migration that calls `migrationBuilder.Sql(...)`.
