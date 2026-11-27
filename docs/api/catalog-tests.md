# Super Admin catalog tests

Both endpoints require a `SUPER_ADMIN` access token. Send a `POST` with an empty body.

| Endpoint | Diagnostic |
| --- | --- |
| `/api/v1/super-admin/providers/{providerId}/test` | Fetches `/models` from the provider's configured API base URL. |
| `/api/v1/super-admin/models/{modelId}/test` | Uses LangChain to send a short request to the configured chat or embedding model. |

A successful test returns `200 OK` with `{"success":true,"latencyMs":123}`. Latency measures the round trip from the API to the private AI service, including the provider call. Tests work on inactive entries. The provider may bill the inference request.

| Status | Meaning |
| --- | --- |
| `401` / `403` | Sign-in or Super Admin access is required. |
| `404` | The provider or model ID was not found. |
| `422` | The provider endpoint or credential needs configuration. |
| `502` | The provider rejected the request or returned an invalid response. |
| `503` | The private AI service or provider is unavailable, or the provider is rate limiting requests. |
| `504` | The test timed out. |

## Read provider health

Send `GET /api/v1/super-admin/providers/{providerId}/health` with a `SUPER_ADMIN` bearer token. The API reads PostgreSQL and returns `404 PROVIDER_NOT_FOUND` for a missing provider. The health read returns `200` for a registered provider, including when its reported status is `unavailable` or `unknown`.

Each completed provider test records its UTC check time, result, and either successful round-trip latency or a safe error category. Model tests leave this history alone. Failures in the private AI service or diagnostic configuration are recorded as inconclusive. Caller cancellations do not create a check.

| Field | Meaning |
| --- | --- |
| `currentStatus` | `healthy` when all conclusive checks in the last 24 hours succeeded. `degraded` when the latest conclusive check succeeded after a failure in the window. `unavailable` when the latest conclusive check failed. `unknown` when the window contains zero conclusive checks. |
| `availabilityPercent24h` | Successful checks divided by conclusive checks, as a percentage. When there are no conclusive checks, the value is `null`. |
| `averageLatencyMs24h` | Mean round-trip latency in milliseconds for successful checks. When none succeeded, the value is `null`. |
| `lastSuccessfulCheckAt`, `lastCheckedAt` | Most recent successful check and most recent completed diagnostic across the full stored history. A missing timestamp is `null`. |
| `recentSafeErrorCategories` | Up to five distinct categories from the 20 most recent failed or inconclusive checks in the last 24 hours, newest first. |

Health updates when provider tests run. The percentage summarizes observed checks in the time window.

## Local setup

Apply the [database scripts](../../infrastructure/database/README.md). The test endpoints use `api_get_provider` and `api_get_model` for their lookups. `api_record_provider_health_check` writes provider checks to `system_log`, and `api_get_provider_health` reads the stored events. Health history follows the system log's retention policy.

Set `AiService:BaseUrl` to the private service address and `AiService:ServiceToken` to a dedicated shared secret in the API's User Secrets. The development default is port `8000`. Start the [private AI service](../../src/ai-service/README.md#7-running-the-private-ai-service) with the same value as `ORCHESTRA_AI_SERVICE_TOKEN`. Add the provider key to the AI service's `.env` and register its public HTTPS base URL and key reference in Orchestra. Test the returned `providerId`. FastAPI reads the key each time, so adding a provider key to the file takes effect on the next test.

Diagnostics use the URL and credential reference from the saved provider record. They reject private and local network destinations.

For hosted NVIDIA models, use `https://integrate.api.nvidia.com/v1` as the provider base URL and `NVIDIA_API_KEY` as the credential reference. Use the provider's model ID as `providerModelKey`. [NVIDIA hosted model catalog](https://docs.nvidia.com/nim-operator/latest/guardrail.html)

The saved provider's `configuration` can specify diagnostic options:

```json
{
  "adapter": "nvidia",
  "testText": "Reply with OK.",
  "testMaxOutputTokens": 12
}
```

`adapter` defaults to `nvidia` for an empty configuration. Use `openai-compatible` for providers whose embedding API accepts the standard request without NVIDIA's `input_type` field. `testText` accepts 1–200 characters and defaults to `Reply with OK.` `testMaxOutputTokens` accepts integers from 1 to 16 for chat tests and defaults to 16. The provider test uses the saved URL, key reference, and timeout to call `GET /models`.
