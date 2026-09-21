"""Mock-HTTP regression tests; never call the native Unity CLI or a live Editor."""
import base64
import importlib.machinery
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

loader = importlib.machinery.SourceFileLoader("afterecho_local_client", str(Path(__file__).with_name("unity-local")))
spec = importlib.util.spec_from_loader(loader.name, loader)
client = importlib.util.module_from_spec(spec)
loader.exec_module(client)


class LocalClientTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.project = Path(self.temporary.name).resolve()
        self.path = self.project / "Library/Pipeline/.unity-pipeline-port"
        self.path.parent.mkdir(parents=True)
        self.token = base64.b64encode(b"T" * 32).decode()  # Synthetic fixture, never an Editor token.
        self.descriptor = {"projectPath": str(self.project), "mode": "editor", "port": 7800,
                           "pid": os.getpid(), "evalToken": self.token, "capabilities": ["exec.argv"]}
        self.save_descriptor()
        self.response = mock.Mock(status=200)
        self.response.read.return_value = b'{"success":true,"result":2}'
        self.connection = mock.Mock()
        self.connection.getresponse.return_value = self.response
        self.factory = self.enterContext(mock.patch.object(client.http.client, "HTTPConnection", return_value=self.connection))
        self.enterContext(mock.patch.dict(os.environ, {"HTTP_PROXY": "http://remote.invalid:3128",
            "HTTPS_PROXY": "http://remote.invalid:3128", "ALL_PROXY": "socks://remote.invalid:1080",
            "AFTERECHO_UNITY_CLI": "/must-not-run/native-unity"}))
        # Any future subprocess/native fallback fails the test before launching anything.
        self.process_guards = [self.enterContext(mock.patch(name, side_effect=AssertionError("Native process forbidden")))
                               for name in ("subprocess.run", "subprocess.Popen", "os.system", "os.execv", "os.execve")]

    def save_descriptor(self):
        self.path.write_text(json.dumps(self.descriptor))
        self.path.chmod(0o600)

    def invoke(self, *args):
        output = io.StringIO()
        code = client.main(list(args), project_root=self.project, stdout=output)
        self.assertNotIn(self.token, output.getvalue())
        for guard in self.process_guards:
            guard.assert_not_called()
        return code, json.loads(output.getvalue())

    def test_argv_authentication_envelope_and_no_proxy_or_native(self):
        code_text = 'return "quoted value with spaces";'
        code, data = self.invoke("command", "eval", "--code", code_text, "--format", "json")
        self.assertEqual(code, 0)
        self.assertEqual(data["data"]["result"], 2)
        self.factory.assert_called_once_with("127.0.0.1", 7800, timeout=60)
        args, kwargs = self.connection.request.call_args
        self.assertEqual(args, ("POST", "/api/exec"))
        self.assertEqual(json.loads(kwargs["body"])["argv"], ["eval", "--code", code_text])
        self.assertEqual(kwargs["headers"]["Authorization"], "Bearer " + self.token)
        self.assertNotIn("Origin", kwargs["headers"])
        self.connection.close.assert_called_once()

    def test_project_mismatch_stops_before_network(self):
        self.descriptor["projectPath"] = str(self.project / "other")
        self.save_descriptor()
        code, data = self.invoke("status")
        self.assertNotEqual(code, 0)
        self.assertEqual(data["errors"][0]["code"], "PROJECT_MISMATCH")
        self.factory.assert_not_called()

    def test_private_descriptor_required(self):
        self.path.chmod(0o644)
        code, data = self.invoke("status")
        self.assertNotEqual(code, 0)
        self.assertEqual(data["errors"][0]["code"], "UNSAFE_DESCRIPTOR")
        self.factory.assert_not_called()

    def test_descriptor_symlink_rejected(self):
        actual = self.path.with_name("other-descriptor")
        self.path.rename(actual)
        self.path.symlink_to(actual)
        code, _ = self.invoke("status")
        self.assertNotEqual(code, 0)
        self.factory.assert_not_called()

    def test_descriptor_pipe_rejected_without_blocking(self):
        self.path.unlink()
        os.mkfifo(self.path, 0o600)
        code, data = self.invoke("status")
        self.assertNotEqual(code, 0)
        self.assertEqual(data["errors"][0]["code"], "UNSAFE_DESCRIPTOR")
        self.factory.assert_not_called()

    def test_descriptor_owner_required(self):
        with mock.patch.object(client.os, "geteuid", return_value=os.getuid() + 1):
            code, data = self.invoke("status")
        self.assertNotEqual(code, 0)
        self.assertEqual(data["errors"][0]["code"], "UNSAFE_DESCRIPTOR")
        self.factory.assert_not_called()

    def test_editor_port_process_and_token_validation(self):
        for field, value in (("mode", "runtime"), ("port", 443), ("pid", 0), ("evalToken", "invalid")):
            with self.subTest(field=field):
                original = self.descriptor[field]
                self.descriptor[field] = value
                self.save_descriptor()
                code, _ = self.invoke("status")
                self.assertNotEqual(code, 0)
                self.factory.assert_not_called()
                self.descriptor[field] = original

    def test_authentication_rejected_once_without_fallback(self):
        self.response.status = 401
        self.response.read.return_value = json.dumps({"error": self.token}).encode()
        code, data = self.invoke("command", "editor_status")
        self.assertNotEqual(code, 0)
        self.assertEqual(data["errors"][0]["code"], "AUTHENTICATION_FAILED")
        self.factory.assert_called_once()
        self.response.read.assert_not_called()

    def test_redirect_is_not_followed(self):
        self.response.status = 302
        self.response.getheader.return_value = "https://remote.invalid/collect"
        code, data = self.invoke("status")
        self.assertNotEqual(code, 0)
        self.assertEqual(data["errors"][0]["code"], "REDIRECT_FORBIDDEN")
        self.factory.assert_called_once()
        self.response.read.assert_not_called()

    def test_http_and_command_failure_propagate_with_redaction(self):
        for status in (200, 400, 503):
            with self.subTest(status=status):
                self.response.status = status
                self.response.read.return_value = json.dumps({"success": False, "errorDetails": "detail " + self.token,
                    "nested": {"evalToken": self.token, self.token: "diagnostic"}, "retryable": True}).encode()
                code, data = self.invoke("command", "eval", "--code", "invalid code")
                self.assertNotEqual(code, 0)
                self.assertFalse(data["success"])
                self.assertTrue(data["data"]["retryable"])

    def test_target_overrides_and_account_actions_never_connect(self):
        for args in (("auth", "status"), ("doctor",), ("status", "--project-path", "/other"),
                     ("status", "--proxy", "http://remote.invalid"), ("status", "--runtime", "Player")):
            with self.subTest(args=args):
                code, _ = self.invoke(*args)
                self.assertNotEqual(code, 0)
                self.factory.assert_not_called()

    def test_status_list_and_job_paths(self):
        for args, reply, path in ((["status"], {"status": "ready"}, "/api/status"),
            (["list", "--query", "eval_file"], {"commands": []}, "/api/commands?detail=compact&query=eval_file"),
            (["job", "abc_123"], {"state": "completed", "jobId": "abc_123"}, "/api/job?id=abc_123")):
            with self.subTest(args=args):
                self.response.read.return_value = json.dumps(reply).encode()
                code, data = self.invoke(*args)
                self.assertEqual(code, 0)
                self.assertEqual(data["data"]["result"], reply)
                self.assertEqual(self.connection.request.call_args.args, ("GET", path))

    def test_job_failure_and_detached_submission(self):
        self.response.read.return_value = b'{"state":"failed","error":"test failure"}'
        code, _ = self.invoke("job", "abc_123")
        self.assertNotEqual(code, 0)
        self.response.read.return_value = b'{"success":true,"result":{"jobId":"abc_123","state":"queued"}}'
        code, _ = self.invoke("command", "eval", "--code", "return 2;", "--job")
        self.assertEqual(code, 0)
        self.assertTrue(json.loads(self.connection.request.call_args.kwargs["body"])["job"])

    def test_help_is_local_without_descriptor_or_network(self):
        self.path.unlink()
        output = io.StringIO()
        self.assertEqual(client.main(["command", "eval_file", "--help"], project_root=self.project, stdout=output), 0)
        self.assertIn("no native Unity CLI", output.getvalue())
        self.factory.assert_not_called()

    def test_requested_job_cancellation_is_successful(self):
        self.response.read.return_value = b'{"state":"canceled","jobId":"abc_123"}'
        code, _ = self.invoke("job", "cancel", "abc_123")
        self.assertEqual(code, 0)
        self.assertEqual(self.connection.request.call_args.args, ("POST", "/api/job/cancel"))
        self.assertEqual(json.loads(self.connection.request.call_args.kwargs["body"]), {"id": "abc_123"})


if __name__ == "__main__":
    unittest.main()
