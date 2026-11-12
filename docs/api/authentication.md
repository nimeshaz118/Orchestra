# Authentication and department API

The public API is served by ASP.NET Core under `/api/v1`. Start new local databases with [`001_initial_schema.sql`](../../infrastructure/database/migrations/001_initial_schema.sql). The database is the source of truth; after an approved SQL schema change, one developer re-scaffolds and commits the EF model. Do not use EF migrations for these tables.

## Local configuration

The API reads `ConnectionStrings:OrchestraDb`, `Auth:SigningKey`, `Auth:CodeHashKey`, and `Resend:ApiKey` from .NET User Secrets or environment variables. Never put these values in Git. Each signing/hash key must contain at least 32 characters. Access, refresh, and password reset token lifetimes have defaults in `appsettings.json` and can be changed through the `Auth` configuration section.

Run these commands from the repository root, replacing the placeholders with your own local values:

```powershell
dotnet user-secrets set "ConnectionStrings:OrchestraDb" "Host=localhost;Port=5432;Database=orchestra_dev;Username=postgres;Password=YOUR_LOCAL_PASSWORD" --project src/backend/Orchestra.Api
dotnet user-secrets set "Auth:SigningKey" "YOUR_RANDOM_SECRET_AT_LEAST_32_CHARACTERS" --project src/backend/Orchestra.Api
dotnet user-secrets set "Auth:CodeHashKey" "A_DIFFERENT_RANDOM_SECRET_AT_LEAST_32_CHARACTERS" --project src/backend/Orchestra.Api
dotnet user-secrets set "Resend:ApiKey" "YOUR_RESEND_API_KEY" --project src/backend/Orchestra.Api
```

For local Resend testing, route all verification/recovery messages to the mailbox registered with your own Resend key. The account retains the email entered at registration; the mail subject and body identify that account. This override is effective only when `ASPNETCORE_ENVIRONMENT=Development`:

```powershell
dotnet user-secrets set "DevelopmentEmail:Recipient" "YOUR_RESEND_ACCOUNT_EMAIL" --project src/backend/Orchestra.Api
dotnet user-secrets set "DevelopmentEmail:AllowAnyDomain" "true" --project src/backend/Orchestra.Api
```

The recipient override allows local accounts with different email addresses to be tested through one inbox. It does not establish ownership of those account addresses. Each teammate should use their own User Secrets and Resend key/account, or arrange access to the shared inbox if sharing one key. Outside Development, registration requires an active row in `approved_email_domain` and mail goes to the registered account email. Override `Resend:From` with a sender on your verified domain outside local testing.

To bootstrap the first privileged accounts, set these values in User Secrets with separate account email addresses and passwords:

```powershell
dotnet user-secrets set "Bootstrap:SuperAdmin:Email" "YOUR_SUPER_ADMIN_EMAIL" --project src/backend/Orchestra.Api
dotnet user-secrets set "Bootstrap:SuperAdmin:Password" "YOUR_INITIAL_SUPER_ADMIN_PASSWORD" --project src/backend/Orchestra.Api
dotnet user-secrets set "Bootstrap:Admin:Email" "YOUR_ADMIN_EMAIL" --project src/backend/Orchestra.Api
dotnet user-secrets set "Bootstrap:Admin:Password" "YOUR_INITIAL_ADMIN_PASSWORD" --project src/backend/Orchestra.Api
```

Startup inserts each role only if there is no active account for it. Both accounts are marked `must_change_password`; sign in and call `POST /auth/change-password` before using protected APIs. Remove the bootstrap passwords from User Secrets after the initial accounts have been created.

Use HTTPS locally (`https://localhost:7235` in the launch profile). Refresh tokens are stored in `Secure`, `HttpOnly`, `SameSite=None` cookies so the HTTP localhost frontend can call the HTTPS API. Refresh and logout reject browser requests whose `Origin` is not listed in `Frontend:Origins`. Send the access token as `Authorization: Bearer ...` and include credentials on frontend refresh/logout requests. The browser should keep the access token in memory.

## Routes

| Route | Access | Behavior |
| --- | --- | --- |
| `GET /public/departments` | Public | Active departments for registration |
| `POST /auth/register` | Public | Create pending User and send verification code |
| `POST /auth/verify-email` | Public | Consume six-digit code and activate account |
| `POST /auth/resend-verification` | Public | Replace prior code after resend cooldown |
| `POST /auth/login` | Public | Password sign-in; access token and refresh cookie |
| `POST /auth/refresh` | Refresh cookie | Rotate token and return a new access token |
| `POST /auth/logout` | Bearer token or refresh cookie | Revoke access token, revoke session, clear cookie, return logout message |
| `POST /auth/logout-all` | Bearer token | Revoke all own refresh sessions and clear this browser's cookie |
| `GET /auth/session` | Bearer token | Current user identity |
| `POST /auth/forgot-password` | Public | Neutral response; send recovery code for eligible account |
| `POST /auth/verify-reset-code` | Public | Exchange recovery code for short-lived reset token |
| `POST /auth/reset-password` | Reset token | Change password, consume challenge, revoke sessions |
| `POST /auth/change-password` | Bearer token | Change known password and revoke sessions |
| `GET /me` | Bearer token | Own profile |
| `PUT /me/avatar` | Bearer token | Validate and replace own avatar; multipart field `file` |
| `GET /me/avatar` | Bearer token | Read own avatar as PNG |
| `DELETE /me/avatar` | Bearer token | Delete own avatar |
| `GET /me/sessions` | Bearer token | Own active sessions |
| `GET, POST /admin/departments` | Admin or Super Admin | List/create departments |
| `GET, POST /super-admin/departments` | Super Admin | Same department operations |

The user creation endpoints never accept a role. The registered account is always `USER`. The initial Admin and Super Admin accounts are created only through bootstrap.

## Postman walkthrough

Use the `https://localhost:7235` API base URL from the HTTPS launch profile. Trust the local ASP.NET development certificate first. In Postman, enable its cookie jar for this host; the secure `orchestra_refresh` cookie is set by login and sent automatically on refresh and logout. Do not manually add the refresh token to a JSON body. Add `Authorization: Bearer <accessToken>` to the session request.

1. Call `POST /api/v1/auth/login` with JSON `{ "email": "...", "password": "..." }`. Save `accessToken` from the JSON response. Confirm the response's `Set-Cookie` contains `Secure`, `HttpOnly`, `SameSite=None`, and `Path=/api/v1`.
2. Call `GET /api/v1/auth/session` with the bearer token. It should return the current user's profile.
3. Call `POST /api/v1/auth/refresh` with no body. Postman sends its stored cookie; the response contains a new access token and rotates the cookie. Use the new access token on `/auth/session` to confirm it works.
4. To demonstrate reuse detection, copy the cookie value before step 3. After step 3, send another refresh request with the copied old cookie (edit the cookie for this request in Postman's cookie manager). It should return `409` with `SESSION_REUSED`; the remaining refresh tokens in that token family are revoked. Sign in again before continuing.
5. To demonstrate current-device logout, sign in again and call `POST /api/v1/auth/logout`. Postman sends the current cookie and/or bearer token; expect `200` with `{ "message": "Logged out successfully." }` and an expired `orchestra_refresh` cookie. A subsequent request to `/api/v1/me` or `/api/v1/auth/session` with the saved pre-logout bearer token returns `401 Unauthorized`, and a refresh attempt with the saved pre-logout cookie also returns `401`.

For a disabled-account check, set the test account's database status to `INACTIVE` or `SUSPENDED` after login, then call refresh with its stored cookie. It should return `401` and clear the cookie. Restore the status to `ACTIVE` before signing in again. Refresh and logout accept requests without an `Origin` header for Postman; if you send an `Origin`, it must exactly match one of `Frontend:Origins`.

## Tests

```powershell
dotnet test tests/Orchestra.Api.Tests/Orchestra.Api.Tests.csproj
```

The regular endpoint tests use an in-memory database and a fake email sender. An opt-in integration test creates a uniquely named PostgreSQL database, applies the approved SQL schema, checks both privileged roles, departments, verification, refresh rotation/reuse, and password reset, then drops that test database. The configured PostgreSQL account must have `CREATEDB` permission. Run it from the repository root with an admin connection string in the API's User Secrets or `ORCHESTRA_TEST_ADMIN_CONNECTION`:

```powershell
$env:ORCHESTRA_RUN_POSTGRES_TESTS = "true"
dotnet test tests/Orchestra.Api.Tests/Orchestra.Api.Tests.csproj --filter "Category=PostgreSQL"
Remove-Item Env:ORCHESTRA_RUN_POSTGRES_TESTS
```

The integration test still uses a fake email sender. Once a Resend key is configured, send a test code to the account mailbox to verify live delivery.

## Login and session validation

| Endpoint | Failure responses |
| --- | --- |
| POST /auth/login | 400 invalid email or missing/oversized password; 401 wrong credentials; 403 disabled/unverified account after password verification; 429 account lockout or IP rate limit |
| POST /auth/refresh | 401 unknown/expired/revoked token or unavailable owner; 409 replay of a rotated token; 500 database or token issuance failure |
| POST /auth/logout | 401 missing/invalid/expired credentials or mismatched bearer and cookie; 404 unknown refresh session; 500 revocation failure |
| GET /auth/session | 401 invalid/expired JWT or inactive session; 403 unavailable account; 500 lookup failure |

Errors retain the existing `code`, `message`, `requestId`, and `retryable` envelope. Unexpected failures use a safe `INTERNAL_ERROR` response and server-side logging. Login accepts existing passwords without imposing the registration complexity policy, but requires a nonempty password of at most 128 characters and a valid email of at most 320 characters. Five failed passwords lock the account for 15 minutes; the shared authentication IP limiter allows 20 requests per minute.

Access JWTs now include `sid`, identifying the existing refresh-token family. JWT authentication verifies signature, issuer, audience, expiry, user security stamp, role, active session and account state. Existing access tokens without `sid` require a new login. Refresh rotation keeps this family identity, so rotation does not invalidate still-unexpired access tokens. Reuse revokes the family. Ordinary revoked tokens return 401 rather than being classified as reuse.

Logout validates bearer tokens through JWT middleware, checks cookie ownership when both credentials are supplied, and revokes only the matching family. It does not change the global security stamp. Refresh and logout acquire the same PostgreSQL user-row lock inside transactions to prevent a concurrent rotation from surviving logout. Access-token generation occurs before committing rotation, so issuance failures roll back the database changes. No schema migration is required.

## Logout from all devices and avatars

All paths below include the `/api/v1` prefix. These endpoints reuse JWT validation, the active-account check, session-family validation, and the existing error envelope. Missing, forged, expired, or revoked credentials return `401`. The existing account policy returns `403` for disabled/unverified users; accounts requiring a password change may still log out from all devices.

| Request | Success | Other failures |
| --- | --- | --- |
| `POST /auth/logout-all` (no body) | `200`, logout message; current cookie cleared | `500` if session revocation fails |
| `PUT /me/avatar` (multipart `file`) | `200`, `{ "avatarUrl": "/api/v1/me/avatar" }` | `400` missing/empty/malformed file, invalid signature, decode failure or dimensions; `413` oversized; `415` unsupported extension/MIME combination; `500` storage failure |
| `GET /me/avatar` | `200`, `image/png`, `Cache-Control: no-store` | `404` no avatar; `500` storage failure |
| `DELETE /me/avatar` (no body) | `204`, no body | `404` no avatar; `500` deletion failure |

### How the code works

Think of each request as **authenticate -> validate -> change state -> respond**.

- **Logout-all:** `AuthController` obtains `sub` (user ID) and `sid` (session family) from the validated JWT. `AuthService.LogoutAllAsync` locks the user's PostgreSQL row, rechecks the active user/current session, and marks every unrevoked refresh record for that user with `RevokedAt` and `LOGOUT_ALL`. This includes expired unrevoked records; previously revoked history is preserved. The transaction and shared refresh lock prevent a competing rotation from surviving. Subsequent requests using any old access token fail the existing session check immediately. Other users are unaffected. A new login after logout-all creates a new session normally.
- **Upload:** `MeController` reads a bounded multipart request. `AvatarValidator` accepts `.png`, `.jpg`, or `.jpeg`, the matching `image/png` or `image/jpeg` MIME type, at most **2 MiB (2,097,152 bytes)**, and dimensions **1-2048 pixels on each side**. Both sides are checked before allocating pixels. Signature validation and a complete first-frame decode reject renamed or corrupt files. Decoded pixels are re-encoded to a static PNG, stripping uploaded metadata and trailing content. SkiaSharp is the image decoder; its Linux native-assets package supports Linux deployment as well as the Windows development environment.
- **Storage/delete:** `AvatarService` uses EF Core to save the actual PNG bytes in `ai_orchestration.app_user.avatar_data` (`bytea`). The authenticated user ID selects the user row. Upload replaces `avatar_data`; delete sets it to NULL without deleting the account. Database failures use the existing safe `500 INTERNAL_ERROR` handler.

Apply `infrastructure/database/migrations/002_user_avatar.sql` once in pgAdmin against your application database before starting the updated API. Existing databases already have `001_initial_schema.sql`; new databases need both scripts in order. No EF migrations are used. Local avatar files are no longer read or written. Previously uploaded local files are not automatically imported: upload the image again after applying the SQL change. The endpoints and multipart `file` field remain the same.

To inspect saved avatars without dumping image bytes:

```sql
SELECT user_id, octet_length(avatar_data) AS image_bytes, updated_at
FROM ai_orchestration.app_user
WHERE avatar_data IS NOT NULL;
```

Retrieve `GET /api/v1/me/avatar` with a bearer-authenticated request to display the image. Database backups now include avatars, and API restarts/redeployments keep them as long as the database persists.

### Check with Postman

1. Log in and copy `accessToken`. Set **Authorization -> Bearer Token** for avatar requests. Use an active account whose initial password has already been changed.
2. Send `PUT https://localhost:7235/api/v1/me/avatar`. Under **Body -> form-data**, add key **file**, change its type from Text to **File**, and choose a PNG/JPEG. Let Postman generate the multipart Content-Type and boundary. Expect `200`.
3. Send `GET /api/v1/me/avatar` with the same bearer token to view the image. Upload a second image and read again to confirm replacement.
4. Check invalid files: an empty file or text renamed to `.png` with `image/png` gives `400`; a valid image wider/taller than 2048 gives `400`; a file over 2 MiB gives `413`; GIF/SVG or the wrong MIME type gives `415`. Invalid uploads must leave the previous avatar unchanged.
5. Send `DELETE /api/v1/me/avatar`. Expect `204`, then `404` if you repeat it or read the deleted avatar. A different user's token cannot read/delete your avatar through `/me`.
6. Log in twice and save both access tokens and both refresh cookies (separate Postman cookie jars or manually saved values). Send `POST /api/v1/auth/logout-all` with either access token and no body. Expect `200`; both saved access tokens must now receive `401` on `/api/v1/me`. Send each saved refresh cookie to `/api/v1/auth/refresh`: both must receive `401`. Another user's session must still work.
7. Remove the Authorization header to check `401` on upload, delete, and logout-all. A refresh cookie alone is insufficient for these endpoints.

Run `dotnet test tests/Orchestra.Api.Tests/Orchestra.Api.Tests.csproj` for automated success, invalid-input, ownership, storage-failure, and revocation-failure checks. Tests use isolated databases and simulate failed saves/deletes; existing avatars must survive these failures. The PostgreSQL test also verifies the bytes directly and reads the image through a new API host. PostgreSQL integration remains opt-in as described above.

If the earlier `user_avatar` table was already created, rerun the revised `002_user_avatar.sql`: it copies existing images into `app_user.avatar_data` and removes the old table in one transaction.
