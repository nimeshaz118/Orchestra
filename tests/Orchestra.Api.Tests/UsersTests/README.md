# Orchestra API user-session tests

These xUnit integration tests use `WebApplicationFactory<Program>` and the
shared `ApiHost` from `AuthEndpointsTests.cs`. Each test gets an isolated
in-memory EF Core database, seeds an active user, logs in through the real
authentication endpoint, and calls the real controller endpoints.

## Run the tests

From the repository root:

```bash
dotnet test tests/Orchestra.Api.Tests/Orchestra.Api.Tests.csproj
```

Run only the user-session test class:

```bash
dotnet test tests/Orchestra.Api.Tests/Orchestra.Api.Tests.csproj \
  --filter "FullyQualifiedName~UserSessionEndpointsTests"
```

## Covered cases

| Test | Endpoint(s) | What it verifies |
| --- | --- | --- |
| `Get_me_returns_the_authenticated_user` | `GET /api/v1/me` | Returns the profile belonging to the authenticated user. |
| `Get_sessions_returns_the_current_session` | `GET /api/v1/me/sessions` | Returns the active session and marks it as current. |
| `Revoke_others_keeps_current_session_and_revokes_the_rest` | `DELETE /api/v1/me/sessions/others` | Revokes every other active session, returns the count, and preserves the current session. |
| `Revoke_selected_session_revokes_only_the_requested_session` | `DELETE /api/v1/me/sessions/{sessionId}` | Revokes only the requested session, records the revoke reason, and leaves the current session usable. |

The tests also confirm that a revoked session cannot use
`GET /api/v1/me/sessions` and that revoked sessions are removed from the
active-session response.
