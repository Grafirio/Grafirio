"""Durable agent execution with a globally bounded, per-process thread pool."""

import asyncio
import logging
import os
import uuid
from concurrent.futures import ThreadPoolExecutor
from contextlib import asynccontextmanager

from fastapi import APIRouter, HTTPException, Request

from agent_analyzer import AgentAnalyzer
from agent_contract import AgentAnalyzeRequest, ConfigHash, Identifier, json_object
from chart_validation import validate_charts
from data_port import DataPortError, GatewayDataPort
from job_store import JobConflict, JobStore, QueueFull

logger = logging.getLogger(__name__)
router = APIRouter(prefix="/agent/analyze")
DEFAULT_CONCURRENCY = 2
POLL_SECONDS = 0.25


def _positive_env(name, default, maximum):
    value = int(os.getenv(name, str(default)))
    if not 1 <= value <= maximum:
        raise ValueError(f"{name} must be between 1 and {maximum}.")
    return value


class CancellableDataPort:
    def __init__(self, inner, is_active):
        self.inner = inner
        self.is_active = is_active

    @property
    def last_truncated(self):
        return self.inner.last_truncated

    def _check(self):
        if not self.is_active():
            raise DataPortError("Job is no longer active; no further SQL will be submitted.")

    def read_sql(self, sql, params=None, max_rows=None):
        self._check()
        return self.inner.read_sql(sql, params, max_rows)

    def scalar(self, sql, params=None):
        self._check()
        return self.inner.scalar(sql, params)


def execute_analysis(payload, store):
    request = AgentAnalyzeRequest.model_validate(payload)
    context = request.model_dump()

    def is_active():
        job = store.get(request.query_id, context)
        return job is not None and job["status"] == "processing"

    data = GatewayDataPort(request.connection_id, request.company_id, request.query_id,
                           request.config_id, request.config_hash)
    result = AgentAnalyzer(CancellableDataPort(data, is_active)).run_analysis(
        json_object(request.config_json), json_object(request.analysis_params_json))
    if result.get("success"):
        validate_charts(result.get("charts"))
    return result


class JobRunner:
    def __init__(self, store, concurrency, execute=execute_analysis):
        self.store = store
        self.concurrency = concurrency
        self.execute = execute
        self.owner = uuid.uuid4().hex
        self.executor = ThreadPoolExecutor(max_workers=concurrency, thread_name_prefix="agent-analysis")
        self.stopping = asyncio.Event()
        self.tasks = []

    def start(self):
        self.tasks = [asyncio.create_task(self._worker()) for _ in range(self.concurrency)]

    async def close(self):
        # Drain active threads so shutdown never claims to have cancelled SQL.
        self.stopping.set()
        await asyncio.gather(*self.tasks)
        self.executor.shutdown(wait=True)

    async def _worker(self):
        while not self.stopping.is_set():
            try:
                payload = await asyncio.to_thread(self.store.claim, self.owner, self.concurrency)
                if payload is None:
                    try:
                        await asyncio.wait_for(self.stopping.wait(), timeout=POLL_SECONDS)
                    except TimeoutError:
                        pass
                    continue
                future = asyncio.get_running_loop().run_in_executor(self.executor, self.execute, payload, self.store)
                while not future.done():
                    await asyncio.wait({future}, timeout=self.store.lease_seconds / 3)
                    if not future.done():
                        await asyncio.to_thread(self.store.heartbeat, payload["query_id"], self.owner)
                try:
                    result = future.result()
                    if result.get("success"):
                        validate_charts(result.get("charts"))
                except Exception as error:
                    logger.error("Agent analysis execution failed for query %s (%s)",
                                 payload["query_id"], type(error).__name__)
                    result = {"success": False, "error": "Analysis execution failed."}
                await asyncio.to_thread(self.store.finish, payload["query_id"], self.owner, result)
            except Exception:
                logger.exception("Durable job worker failed; lease recovery will mark interrupted work.")
                try:
                    await asyncio.wait_for(self.stopping.wait(), timeout=POLL_SECONDS)
                except TimeoutError:
                    pass


@asynccontextmanager
async def agent_lifespan(app):
    store = JobStore(
        os.getenv("AGENT_JOB_DB_PATH", "./state/agent-jobs.sqlite3"),
        max_jobs=_positive_env("AGENT_MAX_JOBS", 1000, 100000),
        retention_seconds=_positive_env("AGENT_JOB_RETENTION_SECONDS", 86400, 2592000),
        lease_seconds=_positive_env("AGENT_JOB_LEASE_SECONDS", 60, 3600))
    runner = JobRunner(store, _positive_env("AGENT_JOB_CONCURRENCY", DEFAULT_CONCURRENCY, 16))
    app.state.agent_store = store
    app.state.agent_runner = runner
    runner.start()
    try:
        yield
    finally:
        await runner.close()


@router.post("")
async def submit_analysis(payload: AgentAnalyzeRequest, request: Request):
    try:
        result, _ = await asyncio.to_thread(request.app.state.agent_store.submit, payload.model_dump())
        return result
    except JobConflict as error:
        raise HTTPException(status_code=409, detail=str(error)) from error
    except QueueFull as error:
        raise HTTPException(status_code=429, detail=str(error)) from error


async def _get_job(request, query_id, company_id, connection_id, config_id, config_hash, cancel=False):
    context = {"company_id": company_id, "connection_id": connection_id,
               "config_id": config_id, "config_hash": config_hash}
    store = request.app.state.agent_store
    job = await asyncio.to_thread(store.cancel if cancel else store.get, query_id, context)
    if job is None:
        raise HTTPException(status_code=404, detail="Analysis not found.")
    return job


@router.get("/status/{query_id}")
@router.get("/result/{query_id}")
async def get_analysis(request: Request, query_id: Identifier, company_id: Identifier,
                       connection_id: Identifier, config_id: Identifier, config_hash: ConfigHash):
    return await _get_job(request, query_id, company_id, connection_id, config_id, config_hash)


@router.post("/cancel/{query_id}")
async def cancel_analysis(request: Request, query_id: Identifier, company_id: Identifier,
                          connection_id: Identifier, config_id: Identifier, config_hash: ConfigHash):
    return await _get_job(request, query_id, company_id, connection_id, config_id, config_hash, cancel=True)