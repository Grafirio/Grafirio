"""Run each existing suite in a fresh process: several legacy tests replace sys.modules."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

engine = Path(__file__).resolve().parents[2] / "src/ai/ai-service-pycaret-engine"
scripts = {"test_query_spec.py", "test_data_port.py"}
failures = []
tests = sorted(engine.glob("test_*.py"))
if not tests:
    raise SystemExit("No Python tests found")
with tempfile.TemporaryDirectory(prefix="grafirio-tests-") as temp:
    env = dict(os.environ, PYTHONUTF8="1", PYTEST_DISABLE_PLUGIN_AUTOLOAD="1",
               DATA_ANALYSIS_API_URL="http://127.0.0.1:1", INTERNAL_API_KEY="synthetic-test-key",
               AGENT_JOB_DB_PATH=str(Path(temp) / "jobs.sqlite3"),
               TELEMETRY_ARCHIVE_ENDPOINT="", TELEMETRY_ARCHIVE_KEY="",
               MODEL_STORAGE_PATH=str(Path(temp) / "models"))
    for suite in tests:
        command = [sys.executable, suite.name] if suite.name in scripts else [sys.executable, "-m", "pytest", "-q", suite.name]
        print(f"\n=== {suite.name} ===", flush=True)
        if subprocess.run(command, cwd=engine, env=env, check=False).returncode:
            failures.append(suite.name)
if failures:
    raise SystemExit("Failed suites: " + ", ".join(failures))
