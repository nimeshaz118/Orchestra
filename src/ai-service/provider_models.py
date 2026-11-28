import json
import socket
import ssl
from dataclasses import dataclass, field
from http.client import HTTPException as HttpClientError, HTTPSConnection
from ipaddress import IPv6Address, ip_address, ip_network
from time import monotonic
from typing import Any
from urllib.parse import SplitResult
from langchain_core.callbacks import CallbackManagerForLLMRun
from langchain_core.embeddings import Embeddings
from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import AIMessage, BaseMessage, convert_to_openai_messages
from langchain_core.outputs import ChatGeneration, ChatResult
from pydantic import PrivateAttr

MAX_RESPONSE_BYTES = 2 * 1024 * 1024
GLOBAL_IPV6_UNICAST = ip_network("2000::/3")


class ProviderError(Exception):
    def __init__(self, status_code: int, message: str):
        super().__init__(message)
        self.status_code = status_code


class PublicHTTPSConnection(HTTPSConnection):
    def __init__(self, host: str, port: int | None, *, timeout: int):
        self.tls_context = ssl.create_default_context()
        super().__init__(host, self.default_port if port is None else port, timeout=timeout, context=self.tls_context)

    def connect(self) -> None:
        deadline = monotonic() + self.timeout
        addresses = socket.getaddrinfo(self.host, self.port, type=socket.SOCK_STREAM)
        if not addresses or any(not is_public_address(address[4][0]) for address in addresses):
            raise ProviderError(422, "Provider endpoints must resolve to public IP addresses.")

        # Connect to a checked address without a second DNS lookup. TLS still verifies the saved hostname.
        last_error = None
        for family, sock_type, protocol, _, address in addresses:
            remaining = deadline - monotonic()
            if remaining <= 0:
                raise TimeoutError("The provider connection timed out.")
            connection = socket.socket(family, sock_type, protocol)
            connection.settimeout(remaining)
            try:
                connection.connect(address)
                self.sock = self.tls_context.wrap_socket(connection, server_hostname=self.host)
            except OSError as error:
                connection.close()
                last_error = error
            else:
                return
        raise last_error or OSError("The provider connection could not be opened.")


def is_public_address(value: str) -> bool:
    address = ip_address(value)
    if isinstance(address, IPv6Address) and address not in GLOBAL_IPV6_UNICAST:
        return False
    return address.is_global and not address.is_multicast and not address.is_reserved


@dataclass(frozen=True)
class ProviderEndpoint:
    address: SplitResult
    timeout_seconds: int
    api_key: str | None = field(repr=False)

    def request_json(self, path: str, payload: dict[str, Any] | None = None) -> dict[str, Any]:
        headers = {"Accept": "application/json"}
        if self.api_key is not None:
            headers["Authorization"] = f"Bearer {self.api_key}"
        if payload is not None:
            headers["Content-Type"] = "application/json"

        connection = PublicHTTPSConnection(self.address.hostname, self.address.port, timeout=self.timeout_seconds)
        try:
            connection.request("GET" if payload is None else "POST", f"{self.address.path}/{path}",
                               None if payload is None else json.dumps(payload), headers)
            with connection.getresponse() as response:
                if response.status in (408, 504):
                    raise ProviderError(504, "The provider timed out.")
                if response.status == 429 or response.status >= 500:
                    raise ProviderError(503, "The provider is unavailable.")
                if response.status != 200:
                    raise ProviderError(502, "The provider rejected the test.")
                raw = response.read(MAX_RESPONSE_BYTES + 1)

            if len(raw) > MAX_RESPONSE_BYTES:
                raise ValueError("Response exceeds the size limit.")
            result = json.loads(raw)
            if not isinstance(result, dict):
                raise ValueError("Expected a JSON object.")
            return result
        except TimeoutError:
            raise ProviderError(504, "The provider timed out.") from None
        except (OSError, HttpClientError):
            raise ProviderError(503, "The provider is unavailable.") from None
        except ValueError:
            raise ProviderError(502, "The provider returned an invalid response.") from None
        finally:
            connection.close()


class ProviderChatModel(BaseChatModel):
    model: str
    max_output_tokens: int
    _endpoint: ProviderEndpoint = PrivateAttr()

    def __init__(self, endpoint: ProviderEndpoint, model: str, max_output_tokens: int):
        super().__init__(model=model, max_output_tokens=max_output_tokens, cache=False)
        self._endpoint = endpoint

    @property
    def _llm_type(self) -> str:
        return "orchestra-provider-chat"

    def _generate(
        self,
        messages: list[BaseMessage],
        stop: list[str] | None = None,
        run_manager: CallbackManagerForLLMRun | None = None,
        **kwargs: Any,
    ) -> ChatResult:
        payload: dict[str, Any] = {
            "model": self.model,
            "messages": convert_to_openai_messages(messages),
            "max_tokens": self.max_output_tokens,
            "stream": False,
        }
        if stop:
            payload["stop"] = stop
        choices = self._endpoint.request_json("chat/completions", payload).get("choices")
        if not isinstance(choices, list) or not choices or not isinstance(choices[0], dict):
            raise ProviderError(502, "The provider returned an invalid chat response.")
        message = choices[0].get("message")
        if not isinstance(message, dict):
            raise ProviderError(502, "The provider returned an invalid chat response.")
        content = message.get("content")
        try:
            reply = AIMessage(content="" if content is None else content)
        except ValueError:
            raise ProviderError(502, "The provider returned an invalid chat response.") from None
        return ChatResult(generations=[ChatGeneration(message=reply)])


class ProviderEmbeddings(Embeddings):
    def __init__(self, endpoint: ProviderEndpoint, model: str, adapter: str):
        self._endpoint = endpoint
        self._model = model
        self._adapter = adapter

    def embed_query(self, text: str) -> list[float]:
        return self._embed([text], "query")[0]

    def embed_documents(self, texts: list[str]) -> list[list[float]]:
        return self._embed(texts, "passage") if texts else []

    def _embed(self, texts: list[str], input_type: str) -> list[list[float]]:
        payload: dict[str, Any] = {"model": self._model, "input": texts}
        if self._adapter == "nvidia":
            payload["input_type"] = input_type

        data = self._endpoint.request_json("embeddings", payload).get("data")
        if not isinstance(data, list) or len(data) != len(texts):
            raise ProviderError(502, "The provider returned invalid embeddings.")

        embeddings = []
        for item in data:
            vector = item.get("embedding") if isinstance(item, dict) else None
            if not isinstance(vector, list) or not vector or not all(type(value) in (int, float) for value in vector):
                raise ProviderError(502, "The provider returned invalid embeddings.")
            embeddings.append(vector)
        return embeddings
