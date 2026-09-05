"""Durability, ownership fencing, concurrency and cancellation regression tests."""

import asyncio
import multiprocessing
import sqlite3
import threading
from concurrent.futures import ThreadPoolExecutor
from unittest.mock import patch

import pytest

from agent_jobs import CancellableDataPort, JobRunner
from data_port import DataPortError
from job_store import JobConflict, JobStore, QueueFull
from test_agent_security import context, payload


def claim_in_process(path, owner):
    return JobStore(path).claim(owner, 2)


@pytest.fixture
def store(tmp_path):
    return JobStore(tmp_path / "jobs.sqlite3")


def test_restart_retains_queued_and_completed_jobs(store):
    body = payload()
    store.submit(body)
    restarted = JobStore(store.path)
    assert restarted.get("query", context())["status"] == "queued"
    assert restarted.claim("worker", 1) == body
    restarted.finish("query", "worker", {"success": True, "charts": [], "summary": "Done"})
    assert JobStore(store.path).get("query", context())["summary"] == "Done"


def test_startup_preserves_live_lease_and_fails_stale_processing(store):
    store.submit(payload())
    store.claim("worker", 1)
    assert JobStore(store.path).get("query", context())["status"] == "processing"
    with sqlite3.connect(store.path) as connection:
        connection.execute("UPDATE jobs SET lease_until=0")
    restarted = JobStore(store.path)
    job = restarted.get("query", context())
    assert job["status"] == "failed"
    assert job["finished_at"] is not None
    store.finish("query", "worker", {"success": True})
    assert restarted.get("query", context())["status"] == "failed"
    assert restarted.submit(payload())[1] is False


def test_heartbeat_extends_lease_and_checks_owner(store):
    store.submit(payload())
    store.claim("worker", 1)
    assert not store.heartbeat("query", "other")
    assert store.heartbeat("query", "worker")


def test_idempotency_payload_conflict(store):
    assert store.submit(payload())[1] is True
    assert store.submit(payload())[1] is False
    with pytest.raises(JobConflict):
        store.submit(payload(company_id="another"))


def test_global_claim_is_atomic_across_independent_connections(store):
    for index in range(8):
        store.submit(payload(query_id=f"query{index}"))
    stores = [JobStore(store.path) for _ in range(8)]
    with ThreadPoolExecutor(max_workers=8) as pool:
        claims = list(pool.map(lambda entry: entry[1].claim(f"worker{entry[0]}", 2), enumerate(stores)))
    claimed = [claim["query_id"] for claim in claims if claim]
    assert len(claimed) == len(set(claimed)) == 2


def test_global_claim_is_bounded_across_processes(store):
    from concurrent.futures import ProcessPoolExecutor
    for index in range(6):
        store.submit(payload(query_id=f"query{index}"))
    with ProcessPoolExecutor(max_workers=3, mp_context=multiprocessing.get_context("spawn")) as pool:
        futures = [pool.submit(claim_in_process, store.path, f"process{index}") for index in range(6)]
        claimed = [result["query_id"] for future in futures if (result := future.result(timeout=30))]
    assert len(claimed) == len(set(claimed)) == 2


def test_capacity_and_retention_are_bounded(tmp_path):
    store = JobStore(tmp_path / "jobs.sqlite3", max_jobs=1, retention_seconds=5)
    store.submit(payload())
    with pytest.raises(QueueFull):
        store.submit(payload(query_id="second"))
    store.claim("worker", 1)
    store.finish("query", "worker", {"success": False, "error": "Done"})
    with sqlite3.connect(store.path) as connection:
        connection.execute("UPDATE jobs SET finished_at=0")
    assert store.submit(payload(query_id="second"))[1]
    assert store.get("query", context()) is None


def test_pending_confirmation_result_is_persisted(store):
    store.submit(payload())
    store.claim("worker", 1)
    store.finish("query", "worker", {"success": False, "needsClarification": True,
                                      "pendingConfirmations": [{"fromColumn": "CustomerId"}], "error": "Confirm"})
    job = JobStore(store.path).get("query", context())
    assert job["status"] == "needs_clarification"
    assert job["needsClarification"] is True
    assert job["pendingConfirmations"] == [{"fromColumn": "CustomerId"}]


def test_cancellation_does_not_claim_to_interrupt_running_sql(store):
    store.submit(payload())
    store.claim("worker", 1)
    assert store.cancel("query", context())["status"] == "cancelling"
    assert store.claim("another", 1) is None
    store.finish("query", "worker", {"success": True, "charts": [{"secret": "discarded"}]})
    job = store.get("query", context())
    assert job["status"] == "cancelled"
    assert "charts" not in job
    assert "cannot be undone" in job["message"]


def test_cancelled_work_cannot_submit_more_sql():
    from unittest.mock import Mock
    inner = Mock()
    port = CancellableDataPort(inner, lambda: False)
    with pytest.raises(DataPortError):
        port.read_sql("SELECT 1")
    with pytest.raises(DataPortError):
        port.scalar("SELECT 1")
    inner.read_sql.assert_not_called()
    inner.scalar.assert_not_called()


def test_worker_offloads_analysis_and_keeps_event_loop_responsive(store):
    async def scenario():
        started = threading.Event()
        release = threading.Event()
        thread_ids = []

        def execute(body, job_store):
            thread_ids.append(threading.get_ident())
            started.set()
            if not release.wait(5):
                raise RuntimeError("Test execution was not released.")
            return {"success": True, "charts": [], "summary": "Done"}

        store.submit(payload())
        runner = JobRunner(store, 1, execute)
        runner.start()
        try:
            assert await asyncio.wait_for(asyncio.to_thread(started.wait, 3), timeout=4)
            assert thread_ids != [threading.get_ident()]
            assert store.get("query", context())["status"] == "processing"
        finally:
            release.set()
            await asyncio.wait_for(runner.close(), timeout=4)
        assert store.get("query", context())["status"] == "completed"

    asyncio.run(scenario())


def test_oversized_result_fails_with_bounded_payload(store):
    store.submit(payload())
    store.claim("worker", 1)
    with patch("job_store.MAX_RESULT_BYTES", 100):
        store.finish("query", "worker", {"success": True, "summary": "x" * 200})
    job = store.get("query", context())
    assert job["status"] == "failed"
    assert "storage limit" in job["error"]