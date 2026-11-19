import json
import os
from http.client import HTTPException as HttpClientError, HTTPSConnection
from secrets import compare_digest
from typing import Annotated, Literal
from urllib.parse import urlsplit

from fastapi import Depends, FastAPI, Header, HTTPException, Response
from pydantic import BaseModel, Field

app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)

NVIDIA_BASE_URL = "https://integrate.api.nvidia.com/v1"
MAX_RESPONSE_BYTES = 2 * 1024 * 1024
TEST_TEXT = "Reply with OK."


class CatalogTestRequest(BaseModel):
    base_url: str = Field(alias="baseUrl")
    credential_reference: str | None = Field(alias="credentialReference")
    timeout_seconds: int = Field(alias="timeoutSeconds", ge=1, le=600)
    model_key: str | None = Field(alias="modelKey", min_length=1, max_length=200)
    model_type: Literal["CHAT", "EMBEDDING"] | None = Field(alias="modelType")


def require_service_token(authorization: Annotated[str | None, Header()] = None) -> None:
    token = os.getenv("ORCHESTRA_AI_SERVICE_TOKEN")
    if not token or not compare_digest((authorization or "").encode(), f"Bearer {token}".encode()):
        raise HTTPException(401, "Valid service credentials are required.")


@app.post("/internal/v1/catalog/test", status_code=204, dependencies=[Depends(require_service_token)])
def run_catalog_test(request: CatalogTestRequest) -> Response:
    base_url = request.base_url.rstrip("/")
    configured_endpoints = os.getenv("ORCHESTRA_PROVIDER_ENDPOINTS")
    approved = json.loads(configured_endpoints) if configured_endpoints else {NVIDIA_BASE_URL: "NVIDIA_API_KEY"}
    if (base_url not in approved or approved[base_url] != request.credential_reference
            or (request.model_key is None) != (request.model_type is None)):
        raise HTTPException(422, "The test configuration is invalid.")

    endpoint = urlsplit(base_url)
    if endpoint.scheme != "https" or not endpoint.hostname or endpoint.username is not None or endpoint.query or endpoint.fragment:
        raise HTTPException(422, "The configured provider endpoint is invalid.")

    headers = {"Accept": "application/json"}
    if request.credential_reference is not None:
        token = os.getenv(request.credential_reference)
        if not token or not token.isascii() or any(character.isspace() for character in token):
            raise HTTPException(422, "The configured provider credential is unavailable.")
        headers["Authorization"] = f"Bearer {token}"

    body = None
    if request.model_type == "CHAT":
        path, response_field = "chat/completions", "choices"
        body = {"model": request.model_key, "max_tokens": 16, "stream": False,
                "messages": [{"role": "user", "content": TEST_TEXT}]}
    elif request.model_type == "EMBEDDING":
        path, response_field = "embeddings", "data"
        body = {"model": request.model_key, "input": [TEST_TEXT]}
        if base_url == NVIDIA_BASE_URL:
            body["input_type"] = "query"
    else:
        path, response_field = "models", "data"

    if body is not None:
        headers["Content-Type"] = "application/json"

    connection = HTTPSConnection(endpoint.hostname, endpoint.port, timeout=request.timeout_seconds)
    try:
        connection.request("GET" if body is None else "POST", f"{endpoint.path}/{path}",
                           None if body is None else json.dumps(body), headers)
        with connection.getresponse() as response:
            if response.status in (408, 504):
                raise HTTPException(504, "The provider timed out.")
            if response.status == 429 or response.status >= 500:
                raise HTTPException(503, "The provider is unavailable.")
            if response.status != 200:
                raise HTTPException(502, "The provider rejected the test.")
            raw = response.read(MAX_RESPONSE_BYTES + 1)
        if len(raw) > MAX_RESPONSE_BYTES:
            raise ValueError("Response exceeds the size limit.")
        payload = json.loads(raw)
        items = payload.get(response_field) if isinstance(payload, dict) else None
        if not isinstance(items, list) or (body is not None and not items):
            raise ValueError("Invalid provider response.")
        if response_field == "choices" and not isinstance(
            items[0].get("message") if isinstance(items[0], dict) else None, dict
        ):
            raise ValueError("Invalid chat completion.")
        if path == "embeddings" and not (
            isinstance(items[0], dict) and isinstance(items[0].get("embedding"), list) and items[0]["embedding"]
        ):
            raise ValueError("Invalid embedding response.")
    except TimeoutError as error:
        raise HTTPException(504, "The provider timed out.") from error
    except (OSError, HttpClientError) as error:
        raise HTTPException(503, "The provider is unavailable.") from error
    except ValueError as error:
        raise HTTPException(502, "The provider returned an invalid response.") from error
    finally:
        connection.close()

    return Response(status_code=204)
