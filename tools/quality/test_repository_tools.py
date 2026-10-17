"""Exercise the real scanner on synthetic repositories, including redaction and range scope."""
import importlib.util
import io
import json
from contextlib import redirect_stdout
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("scan_secrets", HERE / "scan-secrets.py")
scanner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scanner)


class SecretScanTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="grafirio-scanner-test-")
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("config", "user.name", "Synthetic fixture")
        (self.repo / "safe.txt").write_text("initial\n", encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "-qm", "initial")
        self.base = self.git("rev-parse", "HEAD").strip()
        self.report = self.repo / "report.json"

    def git(self, *arguments):
        return subprocess.check_output(["git", "-C", str(self.repo), *arguments], text=True)

    def add_secret(self):
        # Synthetic provider-shaped value; assembled so this source never contains a token.
        self.secret = "ghp_" + "Ab3dEf6hIj9kLm2nOp5qRs8tUv1xYz4aBc7d"
        (self.repo / "example.txt").write_text("token=" + self.secret, encoding="utf-8")
        self.git("add", "example.txt")

    def test_staged_scan_detects_a_secret_without_leaking_it(self):
        self.add_secret()
        output = io.StringIO()
        with redirect_stdout(output):
            self.assertEqual(scanner.scan(self.repo, self.report), 1)
        content = self.report.read_text(encoding="utf-8")
        self.assertNotIn(self.secret, output.getvalue() + content)
        result = json.loads(content)
        self.assertGreater(result["count"], 0)
        self.assertTrue(any(row["File"] == "example.txt" for row in result["findings"]))
        self.assertTrue(all("Secret" not in row and "Match" not in row for row in result["findings"]))

    def test_range_catches_committed_secret_and_ignores_old_findings(self):
        self.add_secret()
        self.git("commit", "-qm", "synthetic finding")
        with redirect_stdout(io.StringIO()):
            self.assertEqual(scanner.scan(self.repo, self.report, base=self.base), 1)
        clean_base = self.git("rev-parse", "HEAD").strip()
        (self.repo / "safe.txt").write_text("safe change\n", encoding="utf-8")
        self.git("add", "safe.txt")
        self.git("commit", "-qm", "safe change")
        with redirect_stdout(io.StringIO()):
            self.assertEqual(scanner.scan(self.repo, self.report, base=clean_base), 0)

    def test_untracked_local_credentials_are_not_scanned(self):
        self.add_secret()
        self.git("reset", "-q", "HEAD", "example.txt")
        with redirect_stdout(io.StringIO()):
            self.assertEqual(scanner.scan(self.repo, self.report), 0)

    def test_invalid_base_is_rejected(self):
        with self.assertRaises(ValueError):
            scanner.scan(self.repo, self.report, base="HEAD; echo bad")


if __name__ == "__main__":
    unittest.main()
