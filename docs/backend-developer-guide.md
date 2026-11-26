# Backend Developer Guide

This guide explains how the Orchestra backend is organised, where to put new code, how to run it, and how to update the database model.

Read it before you write your first backend feature.

---

## 1. The big picture

The backend is split into **4 projects**. Each project has one job.

```
┌──────────────────────┐
│   Orchestra.Api      │  Receives HTTP requests, returns responses
└──────────┬───────────┘
           │ calls
┌──────────▼───────────┐
│  Orchestra.Business  │  Rules and decisions (the "brain")
└──────────┬───────────┘
           │ calls
┌──────────▼───────────┐
│ Orchestra.DataAccess │  Talks to the database and the email service
└──────────┬───────────┘
           │ uses
┌──────────▼───────────┐
│  Orchestra.Entities  │  Plain classes that mirror the database tables
└──────────────────────┘
```

### The one rule to remember

> **A project may only use the projects below it. Never the ones above it.**

| Project | May use | Must NOT use |
|---|---|---|
| `Orchestra.Api` | Business (and, through it, DataAccess and Entities) | — |
| `Orchestra.Business` | DataAccess, Entities | Api |
| `Orchestra.DataAccess` | Entities | Business, Api |
| `Orchestra.Entities` | nothing | everything else |

If you find yourself needing an "upward" reference, the code is in the wrong project. Ask in the team channel before working around it.

---

## 2. What each project contains

All projects live in `src/backend/`.

### 2.1 `Orchestra.Entities`: database table classes

- One C# class per database table (for example `AppUser`, `Department`, `RoutingPolicy`).
- These files are **generated** by the EF Core scaffold command (see section 5).
- **Do not edit these files by hand.** Your changes will be lost the next time someone scaffolds.
- No logic and no packages. Only properties.

### 2.2 `Orchestra.DataAccess`: database access and external services

| File | What it is | Edit by hand? |
|---|---|---|
| `OrchestraDbContext.cs` | The EF Core database context (generated) | ❌ No, it's generated |
| `OrchestraDbContext.AuthMappings.cs` | Custom mapping that scaffolding cannot produce | ✅ Yes |
| `PostgresProcedures.cs` | Helper for calling PostgreSQL stored procedures | ✅ Yes |
| `Auth/EmailSender.cs` | `IEmailSender` and the Resend email client | ✅ Yes |

### 2.3 `Orchestra.Business`: business logic

This is where most of your code goes. It's organised by **feature folder**:

```
Orchestra.Business/
  Common/            ApiException, ApiMappingProfile (AutoMapper)
  Auth/              AuthService, TokenService, CodeService, SessionValidator, AuthContracts
  Catalog/           CatalogTestsService, AiDiagnosticsClient
  Departments/       DepartmentsService (+ its DTOs)
  Domains/           DomainsService
  FeatureFlags/      FeatureFlagsService
  Models/            ModelsService, ModelContracts
  PlatformSettings/  PlatformSettingsService
  Providers/         ProvidersService, ProviderContracts
  RoutingPolicies/   RoutingPoliciesService, DTOs/
  UserPreference/    UserPreferenceService
  Users/             MeService, AvatarService, AvatarValidator, SessionService, UseCases/
```

A feature folder contains:

- **Service**: a class such as `DepartmentsService` with the feature's methods (list, get, create, update...).
- **DTOs / contracts**: the request and response shapes, such as `DepartmentDto` and `CreateDepartmentRequest`.
- **Use cases** (optional): one class per complex action, for example `RevokeSessionUseCase`.

Business code **must not** use HTTP types such as `IFormFile`, `HttpContext`, `IActionResult` or `ControllerBase`. Those belong in the Api.

### 2.4 `Orchestra.Api`: the web API

```
Orchestra.Api/
  Program.cs          Startup: registers services, JWT, CORS, rate limits, error handling
  Common/ApiError.cs  The standard error response shape
  Modules/<Feature>/  One controller per feature (DepartmentsController, ...)
  appsettings*.json   Configuration
```

A controller should be **thin**:

1. Read the request (route values, body, logged-in user).
2. Call **one** service method.
3. Return the result.

Controllers contain **no business rules and no database queries**.

---

## 3. How to add a new feature (step by step)

Example: a new feature called **Teams**.

### Step 1: database (only if you need a new table or column)

Follow section 5.3: write the SQL, apply it, scaffold. You'll get a new `Team` class in `Orchestra.Entities`.

### Step 2: DTOs in Business

`src/backend/Orchestra.Business/Teams/TeamContracts.cs`

```csharp
namespace Orchestra.Api.Modules.Teams;

public sealed record TeamDto(Guid Id, string Name);
public sealed record CreateTeamRequest(string Name);
```

### Step 3: service in Business

`src/backend/Orchestra.Business/Teams/TeamsService.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Data;

namespace Orchestra.Api.Modules.Teams;

public sealed class TeamsService(OrchestraDbContext db)
{
    public async Task<TeamDto> GetByIdAsync(Guid teamId, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new TeamDto(t.TeamId, t.TeamName))
            .SingleOrDefaultAsync(ct);

        return team ?? throw new ApiException(404, "TEAM_NOT_FOUND", "The team was not found.");
    }
}
```

Rules shown here:
- Always pass the `CancellationToken ct` to database calls.
- Use `AsNoTracking()` for read-only queries.
- Return **DTOs**, never entity classes.
- Signal errors by throwing `ApiException(statusCode, "ERROR_CODE", "User-friendly message")`. `Program.cs` turns it into the standard error response.

### Step 4: controller in Api

`src/backend/Orchestra.Api/Modules/Teams/TeamsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orchestra.Api.Modules.Teams;

[ApiController]
public sealed class TeamsController(TeamsService teams) : ControllerBase
{
    [Authorize(Roles = "ADMIN,SUPER_ADMIN"), HttpGet("api/v1/admin/teams/{teamId:guid}")]
    public async Task<IActionResult> Get(Guid teamId, CancellationToken ct)
        => Ok(await teams.GetByIdAsync(teamId, ct));
}
```

### Step 5: register the service

In `src/backend/Orchestra.Api/Program.cs`, next to the other services:

```csharp
builder.Services.AddScoped<TeamsService>();
```

If you forget this step, the API fails at runtime with "Unable to resolve service for type TeamsService".

### Step 6: test

Add tests in `tests/Orchestra.Api.Tests/` and run `dotnet test` (section 4.5).

### Where does my code go? (cheat sheet)

| I am writing... | Project | Folder |
|---|---|---|
| A new endpoint | Api | `Modules/<Feature>/` |
| A rule ("only admins of the same department can...") | Business | `<Feature>/` |
| A request or response shape (DTO) | Business | `<Feature>/` |
| A LINQ query or stored-procedure call | Business service (for now) | `<Feature>/` |
| Custom EF mapping that scaffolding can't do | DataAccess | `OrchestraDbContext.AuthMappings.cs` |
| A client for an outside service (email, API) | DataAccess (or Business if it needs Business types) | `<Feature>/` |
| A new table or column | SQL file, then scaffold | `infrastructure/database/migrations/` |

---

## 4. How to run the project

### 4.1 One-time setup

| Tool | Version | Check with |
|---|---|---|
| .NET SDK | 10.0.403 or a later 10.0.4xx patch (pinned in `global.json`) | `dotnet --list-sdks` |
| Docker Desktop | with Compose v2 | `docker --version` |
| Node.js | see `.nvmrc` (frontend only) | `node --version` |

Install the EF Core command-line tool (the version is pinned in `dotnet-tools.json`). Run this once, from the repo root:

```powershell
dotnet tool restore
```

For full version details, see [development-setup.md](development-setup.md).

### 4.2 Start the database

Start Docker Desktop and wait for "Engine running". Then, from the repo root:

```powershell
docker compose up -d
```

This starts PostgreSQL on **localhost:5433** and, **on the very first start only**, runs all SQL files in `infrastructure/database/migrations/`.

Useful Docker commands:

| Command | What it does |
|---|---|
| `docker compose up -d` | Start the database |
| `docker compose down` | Stop it (data is kept) |
| `docker compose down -v` | Stop it **and delete all data**. The next `up` re-creates the database from the SQL files. |
| `docker logs orchestra-postgres` | See database logs and errors |

### 4.3 Configuration and secrets

`appsettings.Development.json` already contains a local connection string for the Docker database. Keep real secrets, such as the Resend API key, out of Git. Use User Secrets instead:

```powershell
dotnet user-secrets set "Resend:ApiKey" "<your-key>" --project src/backend/Orchestra.Api
```

For the authentication settings, see [api/authentication.md](api/authentication.md).

### 4.4 Run the API

From the repo root:

```powershell
dotnet run --project src/backend/Orchestra.Api --launch-profile https
```

The API runs at **https://localhost:7235** (and http://localhost:5000). To check it's alive, open https://localhost:7235/health/live.

To run the frontend as well, in `src/frontend/` run `npm ci` once, then `npm run dev`. It runs at http://localhost:5173.

### 4.5 Run the tests

From the repo root:

```powershell
dotnet build
dotnet test
```

By default, the 4 PostgreSQL integration tests are **skipped**. To run them too, start Docker (4.2) and then:

```powershell
$env:ORCHESTRA_RUN_POSTGRES_TESTS = "true"
$env:ORCHESTRA_TEST_ADMIN_CONNECTION = "Host=localhost;Port=5433;Database=orchestra_db;Username=postgres;Password=1234"
dotnet test
```

The tests create their own temporary database and delete it afterwards. Your local data is not touched.

---

## 5. Database and EF Core commands

### 5.1 We use the Database-First approach

The **database is the master**. We change tables with SQL, and EF Core generates the C# classes from the database.

```
SQL file  ──►  PostgreSQL  ──►  dotnet ef dbcontext scaffold  ──►  C# entities + DbContext
```

| ✅ Do | ❌ Don't |
|---|---|
| Change the database with a numbered SQL file | Change tables by hand in pgAdmin without a SQL file |
| Run `dotnet ef dbcontext scaffold` | Run `dotnet ef migrations add` or `dotnet ef database update` (those are for Code-First) |
| Put custom mapping in `OrchestraDbContext.AuthMappings.cs` | Edit `OrchestraDbContext.cs` or the files in `Orchestra.Entities` |

### 5.2 The scaffold command

Run it from the **repo root** (the folder that contains `Orchestra.sln`):

```powershell
dotnet ef dbcontext scaffold "Name=ConnectionStrings:OrchestraDb" Npgsql.EntityFrameworkCore.PostgreSQL --project src/backend/Orchestra.DataAccess --startup-project src/backend/Orchestra.Api --context OrchestraDbContext --context-dir . --context-namespace Orchestra.Api.Data --output-dir ../Orchestra.Entities --namespace Orchestra.Api.Models.Entities --schema ai_orchestration --no-onconfiguring --force
```

What each part means:

| Part | Meaning |
|---|---|
| `"Name=ConnectionStrings:OrchestraDb"` | Read the connection string from the Api's configuration. You don't type the password. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | The database provider (PostgreSQL) |
| `--project src/backend/Orchestra.DataAccess` | **Where to write** `OrchestraDbContext.cs` |
| `--startup-project src/backend/Orchestra.Api` | **Which project to run** to read configuration. It also holds the EF Design package. |
| `--context OrchestraDbContext` | Name of the generated context class |
| `--context-dir .` | Put the context in the root of DataAccess |
| `--context-namespace Orchestra.Api.Data` | Keep the existing namespace |
| `--output-dir ../Orchestra.Entities` | Write the entity classes into the Entities project |
| `--namespace Orchestra.Api.Models.Entities` | Keep the existing namespace |
| `--schema ai_orchestration` | Only read our schema |
| `--no-onconfiguring` | Don't hard-code the connection string in the context |
| `--force` | Overwrite the existing generated files |

> **Why the namespaces start with `Orchestra.Api.*`:** the code used to be one project. The namespaces were kept so existing code and tests didn't have to change. Always use the namespaces shown above. If you leave them out, the build breaks.

You'll see warnings like *"Expression index '...' cannot be scaffolded"*. They are **normal**. Those indexes live in the SQL files, and EF doesn't need them.

### 5.3 Workflow: changing the database

1. **Write a new SQL file** with the next number:
   `infrastructure/database/migrations/006_add_teams.sql`
2. **Register the file** in:
   - `docker-compose.yml`: add a volume line like the existing ones.
   - `tests/Orchestra.Api.Tests/PostgresAuthFlowTests.cs`: add it to the list of migration files.
3. **Apply it to your local database.** Docker only runs the SQL files automatically when the database is first created, so for an existing database run:
   ```powershell
   Get-Content infrastructure/database/migrations/006_add_teams.sql | docker exec -i orchestra-postgres psql -U postgres -d orchestra_db
   ```
4. **Scaffold** (the command in 5.2).
5. **Review** with `git diff`:
   - Only changes related to your SQL should appear.
   - If you **dropped** a table, delete its old entity file yourself. Scaffolding never deletes files.
6. **Build and test** (section 4.5, including the PostgreSQL tests).
7. **Commit everything in one commit:**

| Commit | File |
|---|---|
| ✅ | The new SQL file |
| ✅ | `docker-compose.yml` |
| ✅ | `PostgresAuthFlowTests.cs` (migration list) |
| ✅ | `src/backend/Orchestra.DataAccess/OrchestraDbContext.cs` |
| ✅ | Changed or new files in `src/backend/Orchestra.Entities/` |
| ✅ | The service and controller code that uses the change |
| ❌ | `bin/`, `obj/`, real passwords or API keys |

> **Team rule:** only **one developer scaffolds per schema change** at a time. Two people scaffolding in parallel cause hard merge conflicts in `OrchestraDbContext.cs`.

### 5.4 Other useful EF commands

| Command | Use |
|---|---|
| `dotnet tool restore` | Install or update the `dotnet ef` tool (once per machine) |
| `dotnet ef --version` | Check that the tool works |
| `dotnet ef dbcontext info --project src/backend/Orchestra.DataAccess --startup-project src/backend/Orchestra.Api` | Show which database and provider the context uses (a good connection test) |
| `dotnet ef dbcontext list --project src/backend/Orchestra.DataAccess --startup-project src/backend/Orchestra.Api` | List the DbContext classes |

### 5.5 Troubleshooting

| Problem | Fix |
|---|---|
| `A compatible .NET SDK was not found` | Install .NET SDK 10.0.403 or a later 10.0.4xx patch (`winget install Microsoft.DotNet.SDK.10`) |
| `Could not execute because dotnet-ef was not found` | Run `dotnet tool restore` from the repo root |
| `Project file does not exist` / `No project was found` | You're not in the repo root. `cd` to the folder that contains `Orchestra.sln` |
| `Failed to connect to localhost:5433` | Docker Desktop isn't running or the container is stopped: run `docker compose up -d` |
| Build fails after scaffolding: `type or namespace ... could not be found` | You left out `--namespace` or `--context-namespace`. Re-run the exact command from 5.2. |
| Scaffold shows many unexpected changes | Someone edited generated files by hand. Talk to the team before committing. |
| `Unable to resolve service for type ...Service` at runtime | Register the service in `Program.cs` (`builder.Services.AddScoped<...>()`) |

---

## 6. Quick reference

```powershell
# first time on a machine
dotnet tool restore

# every day
docker compose up -d
dotnet run --project src/backend/Orchestra.Api --launch-profile https

# before you push
dotnet build
dotnet test

# after a database change (from repo root)
dotnet ef dbcontext scaffold "Name=ConnectionStrings:OrchestraDb" Npgsql.EntityFrameworkCore.PostgreSQL --project src/backend/Orchestra.DataAccess --startup-project src/backend/Orchestra.Api --context OrchestraDbContext --context-dir . --context-namespace Orchestra.Api.Data --output-dir ../Orchestra.Entities --namespace Orchestra.Api.Models.Entities --schema ai_orchestration --no-onconfiguring --force
```
