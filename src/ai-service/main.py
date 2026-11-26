import os
import re
from pathlib import Path
from secrets import compare_digest
from typing import Annotated, Literal
from urllib.parse import urlsplit

from dotenv import dotenv_values
from fastapi import Depends, FastAPI, Header, HTTPException, Response
from pydantic import BaseModel, Field

from provider_models import ProviderChatModel, ProviderEmbeddings, ProviderEndpoint, ProviderError

app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)
PROVIDER_ENV_FILE = Path(__file__).with_name(".env")
PROVIDER_KEY_REFERENCE = re.compile(r"[A-Z][A-Z0-9_]*_API_KEY(?:_[A-Z0-9]+)*")
RESERVED_KEY_PREFIXES = ("ORCHESTRA_", "LANGCHAIN_", "LANGSMITH_")


class CatalogTestRequest(BaseModel):
    base_url: str = Field(alias="baseUrl")
    credential_reference: str | None = Field(alias="credentialReference")
    timeout_seconds: int = Field(alias="timeoutSeconds", ge=1, le=600)
    model_key: str | None = Field(alias="modelKey", min_length=1, max_length=200)
    model_type: Literal["CHAT", "EMBEDDING"] | None = Field(alias="modelType")
    adapter: Literal["nvidia", "openai-compatible"] | None = None
    test_text: str | None = Field(default=None, alias="testText", min_length=1, max_length=200)
    test_max_output_tokens: int | None = Field(default=None, alias="testMaxOutputTokens", strict=True, ge=1, le=16)


def require_service_token(authorization: Annotated[str | None, Header()] = None) -> None:
    token = os.getenv("ORCHESTRA_AI_SERVICE_TOKEN")
    if not token or not compare_digest((authorization or "").encode(), f"Bearer {token}".encode()):
        raise HTTPException(401, "Valid service credentials are required.")


@app.get("/internal/v1/health/ready", status_code=204, dependencies=[Depends(require_service_token)])
def readiness() -> Response:
    return Response(status_code=204)


@app.post("/internal/v1/catalog/test", status_code=204, dependencies=[Depends(require_service_token)])
def run_catalog_test(request: CatalogTestRequest) -> Response:
    if (request.model_key is None) != (request.model_type is None):
        raise HTTPException(422, "The test configuration is invalid.")

    endpoint = registered_endpoint(request)
    test_text = request.test_text or "Reply with OK."
    try:
        if request.model_key is None:
            if not isinstance(endpoint.request_json("models").get("data"), list):
                raise ProviderError(502, "The provider returned an invalid model list.")
        elif request.model_type == "CHAT":
            ProviderChatModel(endpoint, request.model_key, request.test_max_output_tokens or 16).invoke(test_text)
        else:
            ProviderEmbeddings(endpoint, request.model_key, request.adapter or "nvidia").embed_query(test_text)
    except ProviderError as error:
        raise HTTPException(error.status_code, str(error)) from None

    return Response(status_code=204)


def registered_endpoint(request: CatalogTestRequest) -> ProviderEndpoint:
    base_url = request.base_url.rstrip("/")
    try:
        address = urlsplit(base_url)
        _ = address.port
    except ValueError:
        raise HTTPException(422, "The configured provider endpoint is invalid.") from None
    if (address.scheme != "https" or not address.hostname or address.username is not None
            or address.query or address.fragment):
        raise HTTPException(422, "The configured provider endpoint is invalid.")

    token = provider_key(request.credential_reference) if request.credential_reference is not None else None
    return ProviderEndpoint(address, request.timeout_seconds, token)


def provider_key(reference: str) -> str:
    if (len(reference) > 150 or not PROVIDER_KEY_REFERENCE.fullmatch(reference)
            or reference.startswith(RESERVED_KEY_PREFIXES)):
        raise HTTPException(422, "The credential reference must name a provider API key.")

    # Reload local provider keys when tested. Deployments can supply the same references via process env.
    try:
        values = dotenv_values(PROVIDER_ENV_FILE, interpolate=False) if PROVIDER_ENV_FILE.is_file() else {}
    except OSError:
        raise HTTPException(422, "The configured provider credential is unavailable.") from None
    token = values[reference] if reference in values else os.getenv(reference)
    if not token or not token.isascii() or any(character.isspace() for character in token):
        raise HTTPException(422, "The configured provider credential is unavailable.")
    return token
