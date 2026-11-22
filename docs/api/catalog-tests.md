# Super Admin catalog tests

Both endpoints require a `SUPER_ADMIN` access token. Send a `POST` with an empty body.

| Endpoint | Diagnostic |
| --- | --- |
| `/api/v1/super-admin/providers/{providerId}/test` | Fetches `/models` from the provider's configured API base URL. |
| `/api/v1/super-admin/models/{modelId}/test` | Sends a short, fixed request to the configured chat or embedding model. |

A successful test returns `200 OK` with `{"success":true,"latencyMs":123}`. Latency measures the round trip from the API to the private AI service, including the provider call. Tests work on inactive entries. The provider may bill the inference request.

| Status | Meaning |
| --- | --- |
| `401` / `403` | Sign-in or Super Admin access is required. |
| `404` | The provider or model ID was not found. |
| `422` | The provider endpoint or credential needs configuration. |
| `502` | The provider rejected the request or returned an invalid response. |
| `503` | The private AI service or provider is unavailable, or the provider is rate limiting requests. |
| `504` | The test timed out. |

## Local setup

Apply the [existing database scripts](../../infrastructure/database/README.md). These endpoints use `api_get_provider` and `api_get_model` for their lookups.

Set `AiService:BaseUrl` to `http://127.0.0.1:8001` and `AiService:ServiceToken` to a dedicated shared secret in the API's User Secrets. Start the [private AI service](../../src/ai-service/README.md#7-running-the-private-ai-service) with the same value as `ORCHESTRA_AI_SERVICE_TOKEN`. Configure provider secrets in the AI service environment.

For hosted NVIDIA models, use `https://integrate.api.nvidia.com/v1` as the provider base URL and `NVIDIA_API_KEY` as the credential reference. Use the provider's model ID as `providerModelKey`. [NVIDIA hosted model catalog](https://docs.nvidia.com/nim-operator/latest/guardrail.html)
