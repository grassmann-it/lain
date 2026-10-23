import contextlib
import io
import json
import unittest
from unittest import mock

import diagnose_reference as diagnostic


class DiagnosticTests(unittest.TestCase):
    def test_secret_and_identity_redaction(self):
        raw = json.dumps({"error": {"code": "denied", "type": "plan", "message": "private-token Bearer other-token person@example.com"}}).encode()
        fields = diagnostic.error_fields(raw, ["private-token"])
        self.assertEqual("denied", fields["code"])
        self.assertEqual("plan", fields["type"])
        self.assertNotIn("private-token", fields["message"])
        self.assertNotIn("other-token", fields["message"])
        self.assertNotIn("person@example.com", fields["message"])

    def test_non_json_body_is_omitted(self):
        self.assertEqual("Non-JSON error body omitted.", diagnostic.error_fields(b"<html>sensitive body</html>", [])["message"])

    def test_wrapped_provider_error(self):
        raw = json.dumps({"error": {"message": json.dumps({"error": {"code": "model-denied", "message": "Access denied"}})}}).encode()
        self.assertEqual("model-denied", diagnostic.error_fields(raw, [])["code"])

    def test_default_has_no_network_or_process(self):
        with mock.patch("sys.argv", ["diagnostic"]), mock.patch.object(diagnostic.orchestration.Host, "start", side_effect=AssertionError("process")), contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(0, diagnostic.main())
        self.assertEqual(0, json.loads(output.getvalue())["inferenceRequests"])


if __name__ == "__main__":
    unittest.main()
