# Orchestra
The platform provides enterprise teams with one governed interface for AI chat and questions about approved documents. It automatically selects eligible models and enforces credits and records usage.  One React application serves User, Admin and Super Admin through role and department configuration.

## Where things live

| Path | Purpose |
| --- | --- |
| `src/backend/Orchestra.Api/Modules/` | API features: Auth, Departments, and Users |
| `src/backend/Orchestra.Api/Data/` and `Models/Entities/` | Database-first EF Core context and generated entities; keep custom partial mappings in `Data/` |
| `src/frontend/` | React application |
| `src/ai-service/` | Python AI service setup and locked dependencies |
| `infrastructure/database/migrations/` | Reviewed SQL schema, starting with `001_initial_schema.sql` |
| `tests/Orchestra.Api.Tests/` | API and PostgreSQL integration tests |
| `docs/api/` | API setup and endpoint notes |
| `docs/development-setup.md` | Local tools and development setup |

Start with [development setup](docs/development-setup.md) for tools, then [authentication API setup](docs/api/authentication.md) for database connection, User Secrets, and endpoints. Run the .NET solution from the repository root.


# Orchestra AI Orchestration Platform — Implementation Context

Last reviewed: 2026-11-06
Project stage: Development foundation  


## 1. How to use this file

This file is the current implementation guide for the Orchestra project. Read it before creating code, changing architecture, adding dependencies, creating database migrations, or changing API behavior.

Rules for coding assistants:

1. Preserve the confirmed decisions in this file.
2. Do not restore requirements that are explicitly removed.
3. Use `infrastructure/database/migrations/001_initial_schema.sql` as the approved database baseline.
4. The project uses EF Core database-first. Apply reviewed SQL changes, then re-scaffold and commit the generated model. Do not create EF migrations for these tables.
5. The current user request takes precedence when it updates a decision recorded here.
6. Keep implementations simple enough for a five-member university project team.
7. Prefer clear code, small modules, documented contracts, and reliable behavior over unnecessary abstractions.
8. Use the existing design system and API conventions. Do not independently redesign the product.
9. Never commit secrets, generated credentials, real email addresses, production data, or provider keys.
10. RAG is a later phase. Do not implement RAG, document ingestion, embeddings, vector search, or citations during the current foundation work unless the user explicitly starts that phase.

## 2. Project summary

**Product name:** Orchestra  
**Project title:** AI Orchestration Platform for Enterprise Teams  
**Project type:** University industry project  
**Team:** Nimesha, Kalmith, Chirath, Anuruth, and Abishek

Orchestra gives employees one governed interface for using approved AI models. It chooses an eligible model, enforces access and usage rules, tracks credits and provider usage, stores a small conversation history, and gives administrators monitoring and control features.

The first release serves **one organization**. Departments exist inside that organization. Multi-organization or multi-tenant management is outside the current scope.

### Main goals

- Provide secure AI chat for employees.
- Route each request to an eligible NVIDIA-hosted model.
- Reduce unnecessary AI cost while maintaining suitable answer quality.
- Enforce user, department, and platform usage limits.
- Reserve, consume, release, and adjust internal credits safely.
- Warn users and administrators before limits are reached.
- Give the Admin and Super Admin useful dashboards, reports, controls, alerts, and audit records.
- Keep sensitive provider keys and business rules on the server.
- Build a foundation that can support RAG later without implementing it now.

### Current non-goals

- Multi-organization management.
- Organization creation, suspension, deletion, or Admin-to-organization assignment.
- SSO, Microsoft Entra ID, MSAL, or social login.
- Manual Admin approval of new registrations.
- RAG, document ingestion, embeddings, pgvector search, or source citations in the current phase.
- A separate frontend application for each role.
- Direct browser access to FastAPI, PostgreSQL, NVIDIA, or Resend.
- A microservice for every business module.

## 3. Confirmed roles and scope

The system has exactly three application roles.

| Role | Scope | Main capabilities |
| --- | --- | --- |
| User | Own account, own conversations, own usage, and allowed department services | Register, verify email, sign in, chat, manage own conversation history, view usage and notifications, update profile and preferences |
| Admin | The single organization's management scope; initially one Admin account | Manage users and departments, allocate credits, configure department services, view organization usage and reports, respond to scoped alerts |
| Super Admin | Platform-wide technical and governance scope | Manage providers/models, global policies, budgets, limits, emergency controls, health, global reports, alerts, and audit logs |

Important scope rules:

- There is one organization in the initial release.
- There is initially one Admin and one Super Admin for the system. The approved SQL schema enforces at most one active account for each privileged role.
- Super Admin can use every Admin endpoint in addition to Super Admin endpoints.
- A department is selected during registration, not during sign-in.
- The backend must enforce role, ownership, department, account status, feature status, and credit rules. Hiding a button in React is not authorization.
- A User must never access another user's conversation or usage record by changing an ID.
- An out-of-scope resource should normally return `404` rather than reveal that the resource exists.

## 4. Confirmed architecture

```text
React + TypeScript
        |
        | HTTPS /api/v1
        v
ASP.NET Core modular monolith
        |-----------------------> PostgreSQL
|                          Application data and reviewed SQL migrations
        |
        | Private /internal/v1 API
        v
Python FastAPI + LangChain
        |
        v
Approved NVIDIA models

ASP.NET Core --------------------> Resend
                                   Verification and password-reset email delivery
```

### Architecture rules

- React communicates only with ASP.NET Core.
- ASP.NET Core is the only public backend.
- ASP.NET Core owns authentication, authorization, business rules, credits, user-visible data, reporting coordination, and audit records. Reviewed SQL files own PostgreSQL schema changes.
- FastAPI is a private AI service. It owns intent/routing logic, LangChain prompt construction, NVIDIA provider calls, normalized AI results, and later token streaming.
- FastAPI must not trust a user ID, role, department ID, or model list received from the browser.
- ASP.NET Core calls FastAPI using a private service credential and supplies already validated policy constraints.
- FastAPI must not accept user refresh tokens.
- During the current non-RAG phase, FastAPI should not write application tables directly. It returns results and usage metadata to ASP.NET Core.
- A future RAG worker may receive limited database access through a separate restricted database account, but schema ownership must remain in the reviewed SQL migration process.
- Resend delivers email only. ASP.NET Core generates, stores, expires, and validates verification codes.
- PostgreSQL is never exposed directly to React.

## 5. Technology baseline

| Area | Selected technology |
| --- | --- |
| Node.js runtime | Node.js 26.13.0 LTS |
| Frontend | React and TypeScript versions generated by Vite and locked in `package-lock.json` |
| UI components | MUI, customized to match the approved Figma design |
| UI routing | React Router |
| Main API | ASP.NET Core Web API on the .NET 10 LTS SDK |
| Data access | Entity Framework Core with Npgsql |
| AI service | Python 3.13.x, FastAPI, Pydantic, LangChain |
| AI provider | Approved NVIDIA-hosted models |
| Database | PostgreSQL 18.x; pgvector may be installed for future use but is not used now |
| Authentication | ASP.NET Core Identity-compatible password hashing, JWT access tokens, rotating refresh tokens |
| Email delivery | Resend |
| Local infrastructure | Local PostgreSQL; Docker Compose can be added later |
| Source control | GitHub using `main`, `develop`, and short-lived feature branches |

### Version rules

- Pin Node.js 26.13.0 LTS with `.nvmrc` and commit `package-lock.json`.
- Use the React and TypeScript versions generated by the approved Vite scaffold and lock them in `package-lock.json`.
- Pin the .NET SDK with `global.json` within the .NET 10 LTS release line.
- Pin Python with `.python-version` to the selected Python 3.13.x patch and declare Python packages in `src/ai-service/pyproject.toml` with `uv.lock`.
- Use PostgreSQL 18.x for local databases.
- Do not silently upgrade a major framework or runtime version.
- When adding a dependency, explain why it is needed and avoid overlapping libraries that solve the same problem.

## 6. Repository structure

The current project folders are:

```text
Orchestra/
  docs/
    api/authentication.md       API routes and local secrets
    development-setup.md        Local tool setup
  infrastructure/database/migrations/
    001_initial_schema.sql      Database-first PostgreSQL baseline
  src/
    backend/Orchestra.Api/
      Common/                   Shared API errors
      Data/                     Scaffolded DbContext and custom partial mapping
      Models/Entities/          Scaffolded EF Core entities
      Modules/
        Auth/                   Authentication and email
        Departments/            Department endpoints
        Users/                  Own profile and sessions
    frontend/                   React application
    ai-service/                 Python dependency setup; service code is pending
  tests/Orchestra.Api.Tests/     API and PostgreSQL integration tests
  Orchestra.sln
```

Add new API features under `Modules/`. Keep generated EF Core files in `Data/` and `Models/Entities/`; put custom behavior in separate partial files so re-scaffolding does not overwrite it.

### Local development

PostgreSQL must be running with the [approved SQL schema](infrastructure/database/migrations/001_initial_schema.sql) applied. Configure the API's connection string and local secrets using the [authentication guide](docs/api/authentication.md).

From the repository root, start the API with:

```powershell
dotnet run --project src/backend/Orchestra.Api --launch-profile https
```

The API listens on `https://localhost:7235`. From `src/frontend/`, run `npm ci` once and `npm run dev` to start React at `http://localhost:5173`. The Python service currently contains dependency setup but no runnable application entry point.

## 7. Backend implementation pattern

Use this dependency direction:

```text
Controller
  -> Application service/use case
      -> Domain rules
          -> Repository/EF Core or typed external client
```

### Controllers

- Read HTTP inputs and authenticated context.
- Call one application use case.
- Return documented HTTP results.
- Do not contain business rules or direct EF Core queries.

### Application services

- Check account status, role, ownership, department scope, current policy, and feature status.
- Apply validation and coordinate transactions.
- Reserve and settle credits around AI calls.
- Create notifications and audit events when required.
- Call FastAPI, Resend, and other controlled dependencies through interfaces.

### Repositories and EF Core

- Use async database calls.
- Use cancellation tokens.
- Use parameterized queries through EF Core/Npgsql.
- Use transactions for balance changes and related ledger records.
- Avoid returning EF entities directly from API controllers.
- Use DTOs for public contracts.

### External clients

- Use typed HTTP clients.
- Set explicit timeouts.
- Propagate the request ID.
- Normalize dependency failures into safe application errors.
- Never log API keys, tokens, passwords, verification codes, or full sensitive prompts.

## 8. API conventions

### Paths

- Public application base path: `/api/v1`.
- Private FastAPI base path: `/internal/v1`.
- Use plural resource names.
- Use `GET` for reads, `POST` for creation or commands, `PATCH` for partial updates, `PUT` for replacing complete drafts/configuration, and `DELETE` for revoke/archive/soft-delete behavior.

### Common status codes

| Status | Meaning |
| --- | --- |
| 200 | Successful read or command |
| 201 | Resource created |
| 202 | Long-running operation accepted |
| 204 | Successful command with no response body |
| 400 | Invalid request or business input |
| 401 | Missing, invalid, or expired authentication |
| 403 | Authenticated but not allowed |
| 404 | Resource absent or outside caller scope |
| 409 | Duplicate request, invalid state transition, or stale version |
| 422 | Field-level validation failure |
| 429 | Rate or verification-attempt limit reached |
| 500 | Unexpected internal error |
| 503 | Required service unavailable |
| 504 | Upstream provider timeout where applicable |

### Standard error response

```json
{
  "code": "CREDIT_LIMIT_REACHED",
  "message": "Your monthly credit limit has been reached.",
  "requestId": "f965fb8d-78ee-47da-a410-a92447fde5be",
  "retryable": false,
  "errors": []
}
```

Rules:

- Generate one request ID at the public API boundary.
- Propagate it through ASP.NET Core, FastAPI, NVIDIA calls, usage records, logs, and audit events.
- Use UTC ISO 8601 timestamps in APIs and storage.
- Paginate list endpoints.
- Validate sorting and filter fields using an allow-list.
- Never return stack traces or internal infrastructure details to React.
- Keep API contracts explicit. Do not invent an endpoint when the current API specification already defines one.

### Canonical API groups

Use the approved API specification for exact request and response fields. The implementation is organized into these groups:

- Public registration data and system status.
- Authentication, verification, refresh, logout, and password reset.
- Current-user profile, avatar, preferences, and sessions.
- Conversations, messages, AI requests, retry, and later streaming/cancellation.
- Personal credit balance, usage, ledger history, and warnings.
- Notifications.
- Admin users, departments, credit allocation, configuration, analytics, reports, audit, and support operations.
- Super Admin provider/model catalogue, eligibility, platform settings, budgets, emergency controls, analytics, alerts, health, and global audit operations.
- Asynchronous report generation and protected downloads.
- Private FastAPI health and chat orchestration endpoints.
- Future RAG endpoints, which remain unimplemented until that phase begins.

### Language conventions

- C#: enable nullable reference types, use dependency injection, use `CancellationToken`, and use `DateTimeOffset`/UTC at boundaries.
- TypeScript: keep strict mode enabled, define API DTO types, and avoid untyped `any` values.
- Python: use type hints and Pydantic models at the FastAPI boundary.
- Use consistent names across TypeScript, C#, Python, API documentation, and tests.
- Keep comments focused on decisions and non-obvious constraints rather than restating code.

## 9. Authentication and account flows

### Registration

1. React loads active departments from the public endpoint.
2. User submits full name, approved company email, password, and department.
3. ASP.NET Core validates the company domain and department.
4. The password is hashed using a supported ASP.NET Core password hasher.
5. The account is created in `PendingVerification` state.
6. ASP.NET Core generates a cryptographically secure six-digit code.
7. Store only an HMAC/hash of the code, never the plain code.
8. Set expiry to 10 minutes.
9. Resend sends the plain code to the user's email.
10. Successful verification consumes the code and activates the account.

There is no manual Admin approval after registration.

### Verification-code rules

- Code length: six digits unless the approved API contract changes it.
- Expiry: 10 minutes.
- One-time use only.
- A new code invalidates previous active codes for the same purpose.
- Compare hashes in fixed time.
- Limit verification attempts and resend frequency.
- Apply rate limits by account/email and network source.
- Do not reveal whether an account exists during password recovery.
- Log safe security events without recording the code.

### Sign-in and sessions

- Validate the password and current account status.
- Issue a short-lived JWT access token.
- Store the access token in frontend memory where practical.
- Store the refresh token in a `Secure`, `HttpOnly`, appropriate `SameSite` cookie.
- Store only the refresh-token hash in PostgreSQL.
- Rotate the refresh token every time it is used.
- Detect reuse of an already rotated/revoked refresh token and revoke the affected token family.
- Recheck account status, role, and current access when refreshing.
- Logout revokes the current refresh token.
- Logout-all revokes all active sessions for the account.
- Password reset revokes existing sessions.
- Token lifetimes must be configurable. Do not hard-code a final lifetime until the team records the approved value.

### Password reset

1. User submits an email address.
2. Always return a neutral response.
3. If the account is eligible, generate and email a 10-minute one-time code.
4. Verifying the code returns a short-lived, single-purpose reset token.
5. Resetting the password consumes the reset authorization and revokes current sessions.

### Profile and preferences

`PATCH /me/preferences` is for allowed personal settings such as language, response preferences, citation display when available, send behavior, and notification preferences. It must not allow a User to change role, department authorization, credit balance, or platform policies.

Avatar uploads must validate maximum size, allowed extensions, actual file signature, and successful image decoding. Generate a safe server-side filename and never execute uploaded content.

## 10. AI chat and orchestration

### Initial delivery strategy

Implement one complete non-streaming request flow first. Add streaming and cancellation after authentication, ownership, credit accounting, persistence, and provider-error handling are reliable.

### Request flow

1. React submits a message to ASP.NET Core with an idempotency/request key.
2. ASP.NET Core validates the JWT, account, role, conversation ownership, department policy, service status, and available credits.
3. ASP.NET Core creates the AI request record and reserves an approved credit allowance in one transaction.
4. ASP.NET Core sends a private request to FastAPI with the request ID, recent history, allowed models, policy constraints, and maximum output tokens.
5. FastAPI classifies the task using simple explainable rules first.
6. FastAPI filters models by enabled state, eligibility, capability, context size, limits, policy, and budget.
7. FastAPI ranks only eligible models using the configured cost, latency, and quality preferences.
8. LangChain calls the selected NVIDIA model through a provider adapter.
9. FastAPI returns normalized content, selected provider/model, token usage, latency, fallback status, and terminal status.
10. ASP.NET Core stores the message/result, records usage, settles or releases the reservation, creates alerts if needed, and returns the response to React.

### Internal AI request requirements

The contract should carry at least:

- `requestId`
- `userId` as trusted context from ASP.NET Core
- `conversationId`
- current message
- bounded recent history
- allowed model identifiers
- routing policy
- maximum output tokens
- policy/configuration version

FastAPI must use only the model choices allowed by ASP.NET Core. It may narrow the list but must not broaden it.

### Provider adapter result

Return a normalized structure containing:

- request ID
- response content or safe failure
- selected provider and model
- input and output tokens when known
- latency
- provider request/reference ID when safely available
- fallback attempts
- completion status
- whether usage or cost remains uncertain

### Provider failures

- Do not silently combine partial output from two models.
- A provider may accept and charge for a request before ASP.NET Core times out.
- Preserve an `Uncertain` attempt when the outcome or cost is unknown.
- Do not automatically release every reserved credit after an uncertain result.
- Reconcile uncertain usage before retrying or adjusting the ledger.
- A fallback is a separate provider attempt and must be recorded separately.
- Show the User a clear retryable or non-retryable state.

## 11. Credits, budgets, and usage

Credits are internal usage units. Provider cost is money. Keep them clearly separate.

### Ledger operations

- `Allocate`: add credits to an account/scope.
- `Reserve`: temporarily hold credits before a metered call.
- `Consume/Settle`: convert the final measured usage into consumed credits.
- `Release`: return an unused reservation.
- `Adjust`: audited administrative correction.

### Required accounting rules

- Use a unique idempotency key for each user action.
- Duplicate submission or settlement must not double-charge.
- Balance and ledger changes must be transactional.
- Concurrent requests must not spend the same remaining credits.
- Do not update a balance without creating its matching immutable ledger record.
- Preserve provider attempts and uncertain usage for later reconciliation.
- Store the pricing/version information used for each settlement.
- Monetary amounts use Sri Lankan rupees with `Rs.` formatting, for example `Rs. 1,500.00`.
- Token counts, request counts, and credit quantities are not currency.

### Threshold behavior

- At 80% usage: create a warning.
- At 95% usage: create a critical warning.
- At 100%: block new metered AI requests according to policy.
- Sign-in, usage viewing, settings, and other non-metered account functions remain available.
- Account suspension and credit exhaustion are separate states.
- Backend enforcement is mandatory even when React already shows a warning or disabled action.

## 12. Admin features

The Admin manages the one organization's normal operations.

- Dashboard with user, request, usage, credit, alert, and recent activity summaries.
- User search, details, activation/disable controls, and safe role/account management within allowed scope.
- Department list, details, member visibility, and configuration.
- Credit allocation and adjustment within policy.
- Organization-wide usage and provider/model summaries.
- Department service controls that may narrow global availability.
- Configuration draft, preview, validation, publish, published-version view, and restore workflow.
- Scoped reports and audit events.
- Notifications and support requests where implemented.

Admin settings can never override a Super Admin hard disable or global limit.

## 13. Super Admin features

The Super Admin manages technical and platform-wide governance for the single-organization deployment.

- Platform status, key metrics, alerts, health, and recent activity.
- Provider and model catalogue with capabilities, pricing, status, eligibility, and configuration.
- Enable/disable confirmation with impact and fallback warnings.
- Global request, token, concurrency, fallback, and default-policy controls.
- Platform and department/user budget controls, credit allocation rules, warning thresholds, and consumption monitoring.
- Feature hard disables and service hard disables with explicit impact, audit, confirmation, recovery, and re-enable workflows.
- Usage, token, credit, money, request-volume, failure, provider/model, department, and date-range reports.
- Audit-log details with actor, action, affected entity, timestamp, result, and previous/new values when available.
- Platform health and provider connection status.

Do not build organization CRUD or Admin-to-organization assignment screens for the current single-organization scope.

## 14. Notifications, reports, and audit

### Notifications

- Use one notification list in the UI; do not divide it into unnecessary categories.
- Store persistent in-app notifications for important usage and system events.
- Email delivery may be used for verification, reset, and explicitly approved security/system notifications.
- Record delivery state where an email or other delivery is attempted.

### Reports

- Enforce role and scope on every report query and download.
- Validate date range, grouping, filters, sorting, and export format.
- Use asynchronous report jobs when generation can exceed a normal request duration.
- Report status may include `Processing`, `Completed`, `Failed`, and `Cancelled`.
- Do not expose generated report files through public predictable URLs.

### Audit records

- Create audit events inside trusted backend services. React cannot submit arbitrary audit events.
- Audit sensitive actions, permission/status changes, credit adjustments, configuration publishing/restoring, provider/model changes, hard disables, and report exports.
- Capture actor, action, affected entity, timestamp, outcome, request ID, and safe previous/new values.
- Avoid logging passwords, tokens, API keys, verification codes, full private prompts, or unnecessary personal data.

## 15. UI implementation rules

Use the approved Figma design as the visual source of truth.

### Visual system

- Azure blue and white theme.
- Do not introduce green as a primary/accent theme color.
- Use clean, boxier enterprise components rather than heavily rounded generic AI cards.
- Keep spacing, typography, buttons, tables, forms, status indicators, and modal structure consistent.
- Monetary values use `Rs.` consistently.
- Support the team's main working resolution around 1900×1200 and responsive smaller layouts.

### Navigation

- One React application for all roles.
- Collapsible left sidebar.
- Use `Recent chats` under the sidebar rather than a separate `Conversations` item.
- `New chat` starts only a new conversation.
- Profile control stays at the bottom-left of the sidebar.
- Profile menu contains `Settings`, `Profile`, `Help`, and `Logout`.
- Do not place Profile or Account as separate sidebar items.
- Bell is the only top-right header control.
- Do not add a top-right credit indicator.

### Settings and knowledge

- Settings sections: Security, Preferences, and General.
- Knowledge is labelled `Authorized` for Users.
- A User cannot upload knowledge files or request upload permission in the current UI.
- RAG/knowledge processing controls remain disabled or out of the implementation until the later phase.

### Authentication screens

- Centered sign-in, registration, code verification, forgot-password code, and reset-password layouts.
- No SSO buttons.
- Department appears in registration, not sign-in.
- Display field validation clearly without decorative red sidebars.

### Required states

- Loading, empty, success, warning, error, maintenance, offline, processing, queued, reconnecting, and access-denied states where relevant.
- Handle `401`, `403`, `404`, `409`, `422`, `429`, `500`, `503`, and provider failure responses.
- Confirm destructive or high-impact actions before submission.
- Distinguish an ordinary disable from an emergency hard disable.
- Explain consequences in confirmations for provider/model disables, limits, suspensions, credit changes, and emergency controls.

## 16. Database status

The approved database-first baseline is `infrastructure/database/migrations/001_initial_schema.sql`. Apply reviewed SQL changes to PostgreSQL, then re-scaffold the EF Core context and entities. Keep `OrchestraDbContext.AuthMappings.cs` when re-scaffolding; it corrects the role relationship inferred from partial unique indexes. The earlier provisional guidance below has been superseded by this baseline.

### Subject areas

The baseline covers these subject areas:

- Identity: users, roles/claims, departments, account status, sessions/refresh tokens.
- Verification: email-verification and password-reset challenges with purpose, hash, expiry, attempts, and consumed state.
- Chat: conversations, messages, AI requests, and provider attempts.
- AI catalogue: providers, models, capabilities, eligibility, pricing versions, and configuration.
- Credits: balances/accounts, allocations, reservations, immutable ledger entries, settlement, and adjustments.
- Usage: request/token/cost measurements and reconciliation state.
- Governance: global settings, department settings, feature/service controls, configuration versions, and publication history.
- Communication: notifications, delivery attempts, alerts, and support requests if included.
- Accountability: audit events and generated report jobs.
- Future RAG only: document metadata, versions, processing jobs, chunks, vectors, and grants.

### Database design rules for finalization

- Use UUIDs unless the approved EER chooses another key strategy.
- Use UTC timestamps.
- Add optimistic concurrency/version fields where configuration can be edited concurrently.
- Use unique constraints for business invariants and idempotency keys.
- Use soft delete for conversations where required; normal reads exclude deleted records.
- Use immutable ledger entries for credit history.
- Add indexes based on actual API filters, ownership checks, foreign keys, status, and timestamps.
- Store provider/model pricing with effective versions so historical usage can be reproduced.
- Store only hashes for refresh tokens and one-time verification codes.
- Keep credits, tokens, request counts, and monetary cost as different values.
- Use explicit decimal precision for money and credit calculations; do not use floating-point values for accounting.
- Reviewed SQL migrations remain the single schema owner for shared application tables; EF Core models are generated from PostgreSQL.

## 17. Configuration and secrets

### Frontend

Only public configuration may use `VITE_` variables, such as:

```env
VITE_API_BASE_URL=http://localhost:5000/api/v1
```

Anything prefixed with `VITE_` can reach the browser bundle. Never place secrets there.

### ASP.NET Core

Use local User Secrets or environment variables for:

- PostgreSQL connection string.
- JWT signing material, issuer, and audience.
- Resend API key and verified sender.
- FastAPI internal base URL and service credential.
- Approved company domain configuration if it is not stored in the finalized database.

### FastAPI

Use environment variables for:

- NVIDIA API key.
- Internal service credential.
- ASP.NET Core callback/base URL where required.
- Safe model/routing defaults.

### Source-control rules

Ignore `.env`, local settings, certificates, keys, database volumes, build output, `node_modules`, Python virtual environments, and IDE-specific files. Commit only `.env.example` files with safe placeholders.

## 18. Logging, privacy, and security

- Use HTTPS outside local development.
- Validate all inputs on the server.
- Use supported password hashing; never encrypt or store a plain password.
- Apply rate limits to public authentication endpoints and metered AI endpoints.
- Restrict CORS to the approved frontend origins.
- Enforce maximum request, message, upload, and output-token sizes.
- Validate file signatures and decode images rather than trusting a filename or browser content type.
- Use least-privilege database and service credentials.
- Mask sensitive values in logs.
- Protect private FastAPI endpoints with networking controls plus a service credential.
- Do not place authorization decisions inside prompts.
- Treat provider output as untrusted content before rendering it.
- Render model text safely; do not allow unsafe HTML execution.
- Record high-impact configuration and governance actions in the audit log.

## 19. Git and Pull Request workflow

### Branches

- `main`: stable release-ready code.
- `develop`: integrated development code.
- `feature/<short-name>`: new feature.
- `fix/<short-name>`: bug fix.
- `chore/<short-name>`: setup or maintenance.
- `docs/<short-name>`: documentation.

Examples:

```text
feature/auth-registration
feature/user-chat
feature/admin-departments
feature/ai-routing
chore/postgres-setup
```

### Collaboration rules

- Start from the latest `develop`.
- Keep branches short-lived.
- Assign one primary owner to a module/file set during a task to reduce merge conflicts.
- Divide a major feature across frontend, API contract, backend use case, AI integration, database work, and meaningful tests when appropriate.
- Agree on contracts before parallel implementation.
- Do not change another member's contract silently.
- Open a Pull Request into `develop`.
- Require at least one teammate review.
- Resolve comments and required checks before squash-merging.
- Use a release Pull Request from `develop` to `main` after integration verification.

Recommended commit format:

```text
feat(auth): add registration validation
fix(credits): prevent duplicate settlement
chore(api): add local health checks
docs(setup): explain environment variables
```

## 20. Testing priorities

Write meaningful tests for business rules and risky integrations.

### Authentication

- Approved and rejected company domains.
- Valid and invalid department registration.
- Verification expiry, one-time use, invalid attempts, and resend invalidation.
- Neutral forgot-password response.
- Refresh rotation, reuse detection, logout, logout-all, disabled account, and password-reset revocation.

### Authorization

- User cannot access another user's conversation.
- Role checks cannot be bypassed by calling APIs directly.
- Admin actions remain inside approved scope.
- Global hard disables override lower-level settings.

### Credits

- Concurrent requests cannot overspend.
- Duplicate requests and settlements do not double-charge.
- Failed calls release only the correct reservation.
- Uncertain provider outcomes remain reconcilable.
- 80%, 95%, and 100% behavior is correct.

### AI integration

- Disabled or ineligible models are never selected.
- FastAPI rejects missing/invalid internal service identity.
- Provider timeout, rate limit, malformed response, partial output, and unavailable-service paths are normalized correctly.
- Fallback attempts are separately recorded.

### UI

- Role-based routes and controls.
- Loading, empty, warning, error, maintenance, and access-denied states.
- Responsive sidebar and main layouts.
- Currency displays use `Rs.` and credits remain distinguishable.

## 21. Implementation order

Follow this order unless the sprint plan explicitly changes it.

1. **Foundation:** repository structure, branches, version files, React, ASP.NET Core, FastAPI, PostgreSQL container, environment examples, health checks, and basic CI.
2. **Data design:** maintain the approved SQL baseline and re-scaffold EF Core after reviewed schema changes.
3. **Authentication:** registration, email verification, login, refresh, logout, password reset, current session, profile, and sessions. Preferences are deferred.
4. **Basic user chat:** conversations, messages, one NVIDIA model, non-streaming response, small history, safe errors, and persistence.
5. **Credits:** allocation, reservation, settlement, release, adjustments, thresholds, blocking, and user usage view.
6. **Admin:** users, departments, credits, configuration workflow, alerts, reports, and scoped audit events.
7. **Super Admin:** provider/model catalogue, eligibility, global controls, budgets, hard disables, health, platform reports, and audit details.
8. **Advanced orchestration:** multiple eligible models, deterministic routing, provider fallback, streaming, cancellation, and reconciliation.
9. **Release hardening:** concurrency, security, accessibility, responsive behavior, deployment, monitoring, backups, and failure recovery.
10. **Future RAG phase:** only after the working non-RAG system is complete and the team explicitly starts it.

## 22. Definition of done

A task is complete when:

- Behavior matches the approved requirement and existing Figma/API contract.
- Backend authorization is enforced.
- Input validation and safe error handling are implemented.
- Relevant business-rule tests pass.
- Secrets and personal data are not committed or logged.
- API documentation/contracts are updated when behavior changes.
- Loading, empty, success, warning, and error UI states are handled where relevant.
- Accessibility and responsive behavior have been checked for UI changes.
- The feature works through the real integration path or its temporary dependency is clearly documented.
- The Pull Request is reviewed and merge conflicts are resolved.

## 23. Open decisions — do not guess

The following items are still pending or must be confirmed from the final design documents:

- Final access-token and refresh-token lifetimes.
- Final approved NVIDIA model list and exact capabilities.
- Final pricing-to-credit conversion and pricing-version data.
- Final hosting combination for the frontend, .NET API, FastAPI, and PostgreSQL.
- Whether scheduled reports and support requests are part of the first working release or a later sprint.
- Exact RAG scope, storage, worker, embedding model, chunking rules, and document types for the future phase.

When work depends on one of these decisions, implement only reversible scaffolding or ask for the approved value. Do not convert an assumption into a permanent contract.

## 24. Removed requirements that must stay removed

- No registration approval queue.
- No SSO.
- No separate organization creation or management flow.
- No multi-tenant organization switching.
- No RAG implementation in the current phase.
- No direct React-to-FastAPI connection.
- No direct React-to-database connection.
- No plain verification codes, refresh tokens, passwords, or provider keys stored in the database.
- No independent Python migrations over the shared SQL-owned application tables.
- No frontend-only authorization.

