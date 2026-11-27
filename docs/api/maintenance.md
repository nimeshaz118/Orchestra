# Platform maintenance

Both maintenance commands require a Super Admin bearer token. Apply the [operations stored procedures](../../infrastructure/database/migrations/005_provide_health_%26_Maintenanace.sql) before starting the updated API. Docker Compose applies the migration when initializing a new volume.

## Start maintenance

Send `POST /api/v1/super-admin/maintenance/start` with a JSON body.

```json
{
  "reason": "Roll out an AI service update",
  "publicMessage": "AI requests are paused for scheduled maintenance.",
  "scope": "ai_only",
  "activeRequestPolicy": "allow_finish"
}
```

`reason` accepts 5–500 characters and `publicMessage` accepts 5–300. An optional `expectedEndAt` timestamp informs clients of the estimate. A Super Admin still ends the session explicitly.

- `ai_only` pauses new AI work. The provider and model diagnostic POSTs are the AI work endpoints currently available.
- `authenticated_operations` pauses protected application endpoints. Public routes, sign-in, token refresh, and Super Admin recovery remain accessible.
- `allow_finish` lets admitted requests run. `cancel` signals cooperative cancellation to affected requests tracked by the API process. An upstream provider may still finish a call it has already accepted.
- The optional `Idempotency-Key` replays the original response for the same actor and payload. Reusing the key with different data returns `409 IDEMPOTENCY_CONFLICT`. Starting a second session returns `409 MAINTENANCE_ALREADY_ACTIVE`.

The `200` response contains the session ID, scope, public message, actor, UTC start time, estimated end time, and a snapshot count of affected active requests in the API process. Affected new requests receive `503 MAINTENANCE_ACTIVE`.

## End maintenance

Send `POST /api/v1/super-admin/maintenance/end` with a `summary` of 5–1000 characters.

```json
{
  "summary": "Deployment finished and readiness checks passed."
}
```

The API checks PostgreSQL for both scopes. It checks the authenticated FastAPI readiness route when AI is configured and requires it for `ai_only`. A broad maintenance session can end with AI listed as unavailable when AI is unconfigured. The readiness probe does not send a provider inference request.

A failed required check returns `409 REQUIRED_SERVICE_NOT_READY` and leaves maintenance active. A missing or already-ended session returns `409 MAINTENANCE_NOT_ACTIVE`. The `200` response includes the session ID, actor, UTC end time, restored services, and any unavailable services.

## Public status and audit

`GET /api/v1/system/status` is public and reports the current maintenance state, public message, affected services, and update time. `operational` means normal request admission has resumed. The procedures store the current session in the reserved `system.maintenance` setting and write its transition history to `audit_log`. The procedures use the same settings row to serialize transitions. The API process tracks requests already running on that instance.
