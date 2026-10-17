"""Gitleaks for staged changes, a commit range, or history. Never scans ignored local files."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def scan(repository, report, base=None, history=False):
    binary = ROOT / ".quality/bin" / ("gitleaks.exe" if sys.platform == "win32" else "gitleaks")
    if not binary.exists():
        raise RuntimeError("Run tools/quality/install-binaries.py gitleaks first")
    repository = Path(repository).resolve()
    if base and not re.fullmatch(r"[a-fA-F0-9]{40}", base):
        raise ValueError("Base must be a full commit SHA")
    if base:
        subprocess.run(["git", "-C", str(repository), "cat-file", "-e", base + "^{commit}"], check=True, capture_output=True)
    command = [str(binary), "git", str(repository), "--config", str(ROOT / ".gitleaks.toml"),
               "--redact=100", "--no-banner", "--no-color", "--ignore-gitleaks-allow",
               "--report-format", "json", "--exit-code", "10", "--timeout", "120"]
    if base:
        command += ["--log-opts", base + "..HEAD"]
    elif not history:
        command += ["--pre-commit", "--staged"]
    report = Path(report).resolve()
    report.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="grafirio-gitleaks-") as temporary:
        raw = Path(temporary) / "raw.json"
        run = subprocess.run(command + ["--report-path", str(raw)], capture_output=True, text=True, encoding="utf-8", errors="replace")
        if run.returncode not in (0, 10):
            # Do not echo scanner output: malformed source could contain sensitive text.
            raise RuntimeError(f"Gitleaks failed with exit {run.returncode}; no success was recorded")
        findings = json.loads(raw.read_text(encoding="utf-8")) if raw.exists() else []
        if run.returncode == 10 and not findings:
            raise RuntimeError("Gitleaks reported findings without a usable report")
        fields = ("RuleID", "File", "StartLine", "EndLine", "Commit", "Fingerprint")
        safe = [{key: item[key] for key in fields if key in item} for item in findings]
        report.write_text(json.dumps({"scope": "history" if history else "range" if base else "staged", "count": len(safe), "findings": safe}, indent=2), encoding="utf-8")
    print(f"Gitleaks: {len(safe)} finding(s). Metadata-only report: {report}")
    return 1 if safe else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--base")
    modes.add_argument("--history", action="store_true")
    parser.add_argument("--repo", default=str(ROOT))
    parser.add_argument("--report", default=str(ROOT / "artifacts/quality/secrets.json"))
    options = parser.parse_args()
    try:
        raise SystemExit(scan(options.repo, options.report, options.base, options.history))
    except (RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2)
