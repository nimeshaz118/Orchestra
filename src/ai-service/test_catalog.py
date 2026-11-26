import json
import os
import socket
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import MagicMock, patch

from fastapi import HTTPException
from fastapi.testclient import TestClient

from main import CatalogTestRequest, app, require_service_token, run_catalog_test
from provider_models import MAX_RESPONSE_BYTES, ProviderError, PublicHTTPSConnection

CATALOG_URL = "https://provider.example.test/custom/v2"


class CatalogTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(os.environ, {
            "ORCHESTRA_AI_SERVICE_TOKEN": "internal-token",
            "CATALOG_API_KEY": "provider-token",
        })
        environment.start()
        self.addCleanup(environment.stop)
        temporary = TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.env_file = Path(temporary.name) / ".env"
        config = patch("main.PROVIDER_ENV_FILE", self.env_file)
        config.start()
        self.addCleanup(config.stop)
        connection = patch("provider_models.PublicHTTPSConnection")
        self.connection = connection.start()
        self.addCleanup(connection.stop)
        self.response = MagicMock(status=200)
        self.response.__enter__.return_value = self.response
        self.connection.return_value.getresponse.return_value = self.response

    def request_body(self, **overrides):
        return {
            "baseUrl": CATALOG_URL,
            "credentialReference": "CATALOG_API_KEY",
            "timeoutSeconds": 5,
            "modelKey": None,
            "modelType": None,
            **overrides,
        }

    def request(self, **overrides):
        return CatalogTestRequest.model_validate(self.request_body(**overrides))

    def test_service_credential_is_required(self):
        require_service_token("Bearer internal-token")
        for authorization in (None, "Bearer wrong-token"):
            with self.subTest(authorization=authorization), self.assertRaises(HTTPException) as error:
                require_service_token(authorization)
            self.assertEqual(error.exception.status_code, 401)

    def test_readiness_requires_service_auth_and_does_not_contact_a_provider(self):
        with TestClient(app) as client:
            path = "/internal/v1/health/ready"
            self.assertEqual(client.get(path).status_code, 401)
            self.assertEqual(client.get(path, headers={"Authorization": "Bearer wrong-token"}).status_code, 401)
            self.assertEqual(client.get(path, headers={"Authorization": "Bearer internal-token"}).status_code, 204)
        self.connection.assert_not_called()

    def test_provider_probe_fetches_models(self):
        self.response.read.return_value = b'{"data": []}'
        self.assertEqual(run_catalog_test(self.request()).status_code, 204)
        self.connection.assert_called_once_with("provider.example.test", None, timeout=5)
        self.connection.return_value.request.assert_called_once_with("GET", "/custom/v2/models", None, {
            "Accept": "application/json", "Authorization": "Bearer provider-token",
        })
        self.connection.return_value.close.assert_called_once()

    def test_model_probes_run_chat_and_embedding_inference(self):
        for model_type, expected_path, reply in (
            ("CHAT", "/custom/v2/chat/completions", b'{"choices": [{"message": {"content": "OK"}}]}'),
            ("EMBEDDING", "/custom/v2/embeddings", b'{"data": [{"embedding": [0.5]}]}'),
        ):
            with self.subTest(model_type=model_type):
                self.response.read.return_value = reply
                request = self.request(modelKey="test/model", modelType=model_type, adapter="nvidia",
                                       testText="Configured model check", testMaxOutputTokens=5)
                self.assertEqual(run_catalog_test(request).status_code, 204)
                method, path, body, headers = self.connection.return_value.request.call_args.args
                self.assertEqual((method, path), ("POST", expected_path))
                self.assertEqual(headers["Content-Type"], "application/json")
                self.assertEqual(headers["Authorization"], "Bearer provider-token")
                payload = json.loads(body)
                self.assertEqual(payload["model"], "test/model")
                if model_type == "CHAT":
                    self.assertFalse(payload["stream"])
                    self.assertEqual(payload["max_tokens"], 5)
                    self.assertEqual(payload["messages"], [{"role": "user", "content": "Configured model check"}])
                else:
                    self.assertEqual(payload["input_type"], "query")
                    self.assertEqual(payload["input"], ["Configured model check"])

    def test_embedding_dialect_is_selected_by_configuration(self):
        self.response.read.return_value = b'{"data": [{"embedding": [0.5]}]}'
        for adapter, expected_input_type in ((None, "query"), ("openai-compatible", None)):
            with self.subTest(adapter=adapter):
                self.assertEqual(run_catalog_test(self.request(
                    modelKey="test/model", modelType="EMBEDDING", adapter=adapter)).status_code, 204)
                payload = json.loads(self.connection.return_value.request.call_args.args[2])
                self.assertEqual(payload.get("input_type"), expected_input_type)

    def test_invalid_urls_and_non_provider_secrets_never_send_a_probe(self):
        for overrides in ({"baseUrl": "http://provider.example.test"},
                          {"baseUrl": "https://user:password@provider.example.test/v1"},
                          {"credentialReference": "ORCHESTRA_AI_SERVICE_TOKEN"},
                          {"credentialReference": "LANGSMITH_API_KEY"},
                          {"credentialReference": "CATALOG_API_KEY_"},
                          {"modelKey": "test/model"}):
            with self.subTest(overrides=overrides), self.assertRaises(HTTPException) as error:
                run_catalog_test(self.request(**overrides))
            self.assertEqual(error.exception.status_code, 422)
        self.connection.assert_not_called()

    def test_key_added_to_dotenv_is_loaded_and_two_keys_can_share_one_url(self):
        self.response.read.return_value = b'{"data": []}'
        self.env_file.write_text("CATALOG_API_KEY=first-local-key\n", encoding="utf-8")
        path = "/internal/v1/catalog/test"
        headers = {"Authorization": "Bearer internal-token"}
        second = self.request_body(credentialReference="CATALOG_API_KEY_API_KEY2")
        with TestClient(app) as client:
            self.assertEqual(client.post(path, json=second, headers=headers).status_code, 422)
            self.env_file.write_text(
                "CATALOG_API_KEY=first-local-key\nCATALOG_API_KEY_API_KEY2=second-local-key\n", encoding="utf-8"
            )
            self.assertEqual(client.post(path, json=second, headers=headers).status_code, 204)
            self.assertEqual(client.post(path, json=self.request_body(), headers=headers).status_code, 204)
        requests = self.connection.return_value.request.call_args_list
        self.assertEqual([call.args[3]["Authorization"] for call in requests],
                         ["Bearer second-local-key", "Bearer first-local-key"])

    def test_missing_credential_never_sends_a_probe(self):
        del os.environ["CATALOG_API_KEY"]
        with self.assertRaises(HTTPException) as error:
            run_catalog_test(self.request())
        self.assertEqual(error.exception.status_code, 422)
        self.connection.assert_not_called()

    def test_upstream_failures_have_safe_statuses(self):
        for upstream, expected in ((302, 502), (401, 502), (404, 502), (429, 503), (500, 503), (504, 504)):
            with self.subTest(upstream=upstream), self.assertRaises(HTTPException) as error:
                self.response.status = upstream
                run_catalog_test(self.request())
            self.assertEqual(error.exception.status_code, expected)
        self.response.read.assert_not_called()

    def test_malformed_response_and_socket_failures(self):
        self.response.read.return_value = b'{"unexpected": []}'
        with self.assertRaises(HTTPException) as error:
            run_catalog_test(self.request())
        self.assertEqual(error.exception.status_code, 502)

        for failure, expected in ((ConnectionError, 503), (TimeoutError, 504)):
            with self.subTest(failure=failure), self.assertRaises(HTTPException) as error:
                self.connection.return_value.request.side_effect = failure
                run_catalog_test(self.request())
            self.assertEqual(error.exception.status_code, expected)

    def test_langchain_paths_preserve_error_codes_and_response_limit(self):
        request = self.request(modelKey="test/model", modelType="CHAT")
        self.response.status = 429
        with self.assertRaises(HTTPException) as error:
            run_catalog_test(request)
        self.assertEqual(error.exception.status_code, 503)

        self.response.status = 200
        self.response.read.return_value = b"x" * (MAX_RESPONSE_BYTES + 1)
        with self.assertRaises(HTTPException) as error:
            run_catalog_test(request)
        self.assertEqual(error.exception.status_code, 502)
        self.response.read.assert_called_once_with(MAX_RESPONSE_BYTES + 1)


class PublicHTTPSConnectionTests(unittest.TestCase):
    def test_dns_results_with_nonpublic_addresses_are_rejected_before_opening_a_socket(self):
        public = (socket.AF_INET, socket.SOCK_STREAM, socket.IPPROTO_TCP, "", ("8.8.8.8", 443))
        for address in ("127.0.0.1", "10.0.0.1", "169.254.169.254", "224.0.0.1", "::1", "64:ff9b::a00:1"):
            with self.subTest(address=address):
                private = (socket.AF_INET, socket.SOCK_STREAM, socket.IPPROTO_TCP, "", (address, 443))
                connection = PublicHTTPSConnection("provider.example.test", 443, timeout=5)
                with patch("provider_models.socket.getaddrinfo", return_value=[public, private]), \
                        patch("provider_models.socket.socket") as create_socket:
                    with self.assertRaises(ProviderError) as error:
                        connection.connect()
                    self.assertEqual(error.exception.status_code, 422)
                    create_socket.assert_not_called()

    def test_connection_uses_the_checked_address_and_the_original_tls_hostname(self):
        address = ("8.8.8.8", 443)
        dns = [(socket.AF_INET, socket.SOCK_STREAM, socket.IPPROTO_TCP, "", address)]
        with patch("provider_models.ssl.create_default_context") as context:
            connection = PublicHTTPSConnection("provider.example.test", None, timeout=5)
            with patch("provider_models.socket.getaddrinfo", return_value=dns) as resolve, \
                    patch("provider_models.socket.socket") as create_socket:
                connection.connect()
                resolve.assert_called_once_with("provider.example.test", 443, type=socket.SOCK_STREAM)
                create_socket.return_value.connect.assert_called_once_with(address)
                context.return_value.wrap_socket.assert_called_once_with(
                    create_socket.return_value, server_hostname="provider.example.test"
                )


if __name__ == "__main__":
    unittest.main()
