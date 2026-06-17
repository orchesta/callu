# Contributing to CalluApp

Thanks for considering a contribution to CalluApp. It's a small, self-hosted project, and help is welcome.

## 💻 Local Development Setup

To get your local environment ready for coding:

1. **Prerequisites:**
   - [.NET 10 SDK](https://dotnet.microsoft.com/download)
   - [Node.js](https://nodejs.org/) (v20 or newer)
   - Docker & Docker Compose
2. **Setup PostgreSQL:** 
   From the repository root, start the database:
   ```bash
   docker-compose up -d db
   ```
3. **Start the API (Backend):** 
   ```bash
   cd src/Callu.Api
   dotnet run
   ```
   *Note: Our application automatically runs `dbContext.Database.MigrateAsync()` on startup, so it will create the tables for you.*

   > ⚠️ **Warning:**
   > Database migrations are executed automatically at application startup.
   > A faulty migration will prevent the application from starting and cause a crash loop.
   > Always test migrations against a database containing real-like data before submitting a PR.
4. **Start the Web UI (Frontend):** 
   ```bash
   cd src/Callu.Web
   npm install
   npm run dev
   ```

---

## 🏛️ The "Golden Rules" of Database Migrations

CalluApp is distributed as a self-hosted Docker container, so an existing instance may upgrade straight to your code with live data in its database. **Because migrations run automatically at startup, a destructive change can break that upgrade.**

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
3. **Tone:** CalluApp maintains a "humble, non-exaggerated, and technically accurate" tone in its UI and Documentation. Avoid over-promising or using extreme marketing language ("World's fastest", "Incredible", etc.).
4. **Validation:** Before submitting, please verify:
   - `dotnet build` succeeds with zero errors.
   - `npm run build` inside `Callu.Web` succeeds without Typescript errors.
