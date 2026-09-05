"""SQLite queue for multiple processes on ONE host and one persistent local file."""

import hashlib
import json
import sqlite3
import time
from contextlib import contextmanager
from pathlib import Path

ACTIVE_STATUSES = ("processing", "cancelling")
TERMINAL_STATUSES = ("completed", "failed", "needs_clarification", "cancelled")
DEFAULT_MAX_JOBS = 1000
DEFAULT_RETENTION_SECONDS = 86400
DEFAULT_LEASE_SECONDS = 60
MAX_RESULT_BYTES = 4 * 1024 * 1024


class JobConflict(ValueError):
    pass


class QueueFull(ValueError):
    pass


class JobStore:
    def __init__(self, path, max_jobs=DEFAULT_MAX_JOBS,
                 retention_seconds=DEFAULT_RETENTION_SECONDS, lease_seconds=DEFAULT_LEASE_SECONDS):
        if path == ":memory:" or min(max_jobs, retention_seconds, lease_seconds) <= 0:
            raise ValueError("A persistent SQLite path and positive limits are required.")
        self.path = str(path)
        self.max_jobs = max_jobs
        self.retention_seconds = retention_seconds
        self.lease_seconds = lease_seconds
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        with self._connection() as connection:
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("""CREATE TABLE IF NOT EXISTS jobs (
                query_id TEXT PRIMARY KEY, company_id TEXT NOT NULL,
                connection_id TEXT NOT NULL, config_id TEXT NOT NULL, config_hash TEXT NOT NULL,
                payload_hash TEXT NOT NULL, payload TEXT NOT NULL,
                status TEXT NOT NULL, message TEXT NOT NULL, result TEXT,
                created_at REAL NOT NULL, updated_at REAL NOT NULL,
                started_at REAL, finished_at REAL, lease_until REAL, owner TEXT
            )""")
            connection.execute("CREATE INDEX IF NOT EXISTS jobs_status_created ON jobs(status, created_at)")
        self.recover()

    @contextmanager
    def _connection(self):
        connection = sqlite3.connect(self.path, timeout=10, isolation_level=None)
        connection.row_factory = sqlite3.Row
        try:
            yield connection
        finally:
            connection.close()

    def _maintenance(self, connection, now):
        connection.execute("""UPDATE jobs SET status='failed',
            message='Worker interrupted; submit a new query ID to retry.',
            updated_at=?, finished_at=?, owner=NULL, lease_until=NULL
            WHERE status IN ('processing','cancelling') AND lease_until < ?""", (now, now, now))
        connection.execute("""DELETE FROM jobs WHERE status IN
            ('completed','failed','needs_clarification','cancelled') AND finished_at < ?""",
                           (now - self.retention_seconds,))

    def recover(self):
        with self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            self._maintenance(connection, time.time())
            connection.commit()

    def submit(self, payload):
        encoded = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False)
        fingerprint = hashlib.sha256(encoded.encode("utf-8")).hexdigest()
        now = time.time()
        with self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            self._maintenance(connection, now)
            existing = connection.execute("SELECT * FROM jobs WHERE query_id=?", (payload["query_id"],)).fetchone()
            if existing:
                if existing["payload_hash"] != fingerprint:
                    raise JobConflict("Query ID already belongs to a different payload.")
                connection.commit()
                return self._public(existing), False
            if connection.execute("SELECT COUNT(*) FROM jobs").fetchone()[0] >= self.max_jobs:
                raise QueueFull("Job storage capacity reached; retry after retention cleanup.")
            connection.execute("""INSERT INTO jobs
                (query_id,company_id,connection_id,config_id,config_hash,payload_hash,payload,
                 status,message,created_at,updated_at) VALUES (?,?,?,?,?,?,?,'queued','Analysis queued',?,?)""",
                               (*(payload[key] for key in ("query_id", "company_id", "connection_id", "config_id", "config_hash")),
                                fingerprint, encoded, now, now))
            row = connection.execute("SELECT * FROM jobs WHERE query_id=?", (payload["query_id"],)).fetchone()
            connection.commit()
            return self._public(row), True

    def claim(self, owner, concurrency):
        now = time.time()
        with self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            self._maintenance(connection, now)
            active = connection.execute("SELECT COUNT(*) FROM jobs WHERE status IN ('processing','cancelling')").fetchone()[0]
            row = connection.execute("SELECT * FROM jobs WHERE status='queued' ORDER BY created_at LIMIT 1").fetchone()
            if active >= concurrency or row is None:
                connection.commit()
                return None
            connection.execute("""UPDATE jobs SET status='processing',message='Analysis running',
                owner=?,lease_until=?,started_at=?,updated_at=? WHERE query_id=?""",
                               (owner, now + self.lease_seconds, now, now, row["query_id"]))
            connection.commit()
            return json.loads(row["payload"])

    def heartbeat(self, query_id, owner):
        now = time.time()
        with self._connection() as connection:
            return connection.execute("""UPDATE jobs SET lease_until=?,updated_at=?
                WHERE query_id=? AND owner=? AND status IN ('processing','cancelling')""",
                                      (now + self.lease_seconds, now, query_id, owner)).rowcount == 1

    def finish(self, query_id, owner, result):
        status = "needs_clarification" if result.get("needsClarification") else "completed" if result.get("success") else "failed"
        encoded = json.dumps(result, ensure_ascii=False, allow_nan=False)
        if len(encoded.encode("utf-8")) > MAX_RESULT_BYTES:
            result = {"success": False, "error": "Analysis result exceeds storage limit."}
            encoded, status = json.dumps(result), "failed"
        message = result.get("error") or result.get("summary") or status
        now = time.time()
        with self._connection() as connection:
            connection.execute("""UPDATE jobs SET
                status=CASE WHEN status='cancelling' THEN 'cancelled' ELSE ? END,
                message=CASE WHEN status='cancelling' THEN 'Cancelled; completed SQL cannot be undone.' ELSE ? END,
                result=CASE WHEN status='cancelling' THEN NULL ELSE ? END,
                updated_at=?,finished_at=?,owner=NULL,lease_until=NULL
                WHERE query_id=? AND owner=? AND status IN ('processing','cancelling')""",
                               (status, message, encoded, now, now, query_id, owner))

    def _scoped(self, connection, query_id, context):
        return connection.execute("""SELECT * FROM jobs WHERE query_id=? AND company_id=?
            AND connection_id=? AND config_id=? AND config_hash=?""",
                                  (query_id, *(context[key] for key in ("company_id", "connection_id", "config_id", "config_hash")))).fetchone()

    def get(self, query_id, context):
        with self._connection() as connection:
            row = self._scoped(connection, query_id, context)
            return self._public(row) if row else None

    def cancel(self, query_id, context):
        now = time.time()
        with self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = self._scoped(connection, query_id, context)
            if row is None:
                connection.commit()
                return None
            if row["status"] == "queued":
                connection.execute("UPDATE jobs SET status='cancelled',message='Cancelled before execution',updated_at=?,finished_at=? WHERE query_id=?", (now, now, query_id))
            elif row["status"] == "processing":
                connection.execute("UPDATE jobs SET status='cancelling',message='Cancellation requested; running SQL is not interrupted.',updated_at=? WHERE query_id=?", (now, query_id))
            row = self._scoped(connection, query_id, context)
            connection.commit()
            return self._public(row)

    @staticmethod
    def _public(row):
        result = json.loads(row["result"]) if row["result"] else {}
        result.update({key: row[key] for key in (
            "query_id", "company_id", "connection_id", "config_id", "config_hash",
            "status", "message", "created_at", "updated_at", "started_at", "finished_at")})
        result["request_id"] = json.loads(row["payload"])["request_id"]
        result["progress"] = 100 if row["status"] == "completed" else 0
        return result