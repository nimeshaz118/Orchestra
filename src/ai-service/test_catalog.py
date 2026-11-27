import json
import os
import unittest
from unittest.mock import MagicMock, patch

from fastapi import HTTPException

from main import CatalogTestRequest, NVIDIA_BASE_URL, require_service_token, run_catalog_test


class CatalogTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(os.environ, {
            "ORCHESTRA_AI_SERVICE_TOKEN": "internal-token",
            "ORCHESTRA_PROVIDER_ENDPOINTS": json.dumps({NVIDIA_BASE_URL: "NVIDIA_API_KEY"}),
            "NVIDIA_API_KEY": "provider-token",
        })
        environment.start()
        self.addCleanup(environment.stop)
        connection = patch("main.HTTPSConnection")
        self.connection = connection.start()
        self.addCleanup(connection.stop)
        self.response = MagicMock(status=200)
        self.response.__enter__.return_value = self.response
        self.connection.return_value.getresponse.return_value = self.response

    def request(self, **overrides):
        return CatalogTestRequest.model_validate({
            "baseUrl": NVIDIA_BASE_URL,
            "credentialReference": "NVIDIA_API_KEY",
            "timeoutSeconds": 5,
            "modelKey": None,
            "modelType": None,
            **overrides,
        })

    def test_service_credential_is_required(self):
        require_service_token("Bearer internal-token")
        for authorization in (None, "Bearer wrong-token"):
            with self.subTest(authorization=authorization), self.assertRaises(HTTPException) as error:
                require_service_token(authorization)
            self.assertEqual(error.exception.status_code, 401)

    def test_provider_probe_fetches_models(self):
        self.response.read.return_value = b'{"data": []}'
        self.assertEqual(run_catalog_test(self.request()).status_code, 204)
        self.connection.assert_called_once_with("integrate.api.nvidia.com", None, timeout=5)
        self.connection.return_value.request.assert_called_once_with("GET", "/v1/models", None, {
            "Accept": "application/json", "Authorization": "Bearer provider-token",
        })
        self.connection.return_value.close.assert_called_once()

    def test_model_probes_run_chat_and_embedding_inference(self):
        for model_type, expected_path, reply in (
            ("CHAT", "/v1/chat/completions", b'{"choices": [{"message": {"content": "OK"}}]}'),
            ("EMBEDDING", "/v1/embeddings", b'{"data": [{"embedding": [0.5]}]}'),
        ):
            with self.subTest(model_type=model_type):
                self.response.read.return_value = reply
                request = self.request(modelKey="test/model", modelType=model_type)
                self.assertEqual(run_catalog_test(request).status_code, 204)
                method, path, body, headers = self.connection.return_value.request.call_args.args
                self.assertEqual((method, path), ("POST", expected_path))
                self.assertEqual(headers["Content-Type"], "application/json")
                payload = json.loads(body)
                self.assertEqual(payload["model"], "test/model")
                if model_type == "CHAT":
                    self.assertFalse(payload["stream"])
                    self.assertEqual(payload["max_tokens"], 16)
                else:
                    self.assertEqual(payload["input_type"], "query")

    def test_unapproved_url_or_credential_never_sends_a_probe(self):
        for overrides in ({"baseUrl": "https://unapproved.example.test"},
                          {"credentialReference": "ORCHESTRA_AI_SERVICE_TOKEN"},
                          {"modelKey": "test/model"}):
            with self.subTest(overrides=overrides), self.assertRaises(HTTPException) as error:
                run_catalog_test(self.request(**overrides))
            self.assertEqual(error.exception.status_code, 422)
        self.connection.assert_not_called()

    def test_missing_credential_never_sends_a_probe(self):
        del os.environ["NVIDIA_API_KEY"]
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


if __name__ == "__main__":
    unittest.main()
