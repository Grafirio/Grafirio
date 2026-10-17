import asyncio
import json
import logging
import uuid
from unittest.mock import patch

import telemetry_archive as telemetry


def test_logging_export_never_formats_or_sends_private_message():
    handler = telemetry.ArchiveHandler('https://collector.invalid', 'synthetic-key', capacity=2)
    record = logging.LogRecord('engine', logging.ERROR, '', 0, 'SQL password=%s', ('customer-secret',), None)
    handler.emit(record)
    event = handler.queue.get_nowait()
    assert event['name'] == 'application.log'
    assert event['level'] == 'Error'
    assert 'customer-secret' not in json.dumps(event)
    assert 'SQL' not in json.dumps(event)


def test_queue_is_bounded_and_reports_drops_without_blocking():
    handler = telemetry.ArchiveHandler('https://collector.invalid', 'synthetic-key', capacity=1)
    handler.event()
    handler.event()
    assert handler.queue.qsize() == 1
    assert handler.dropped == 1


def test_http_context_keeps_query_and_trace_without_reusing_parent_span_or_body():
    handler = telemetry.ArchiveHandler('https://collector.invalid', 'synthetic-key')
    query = str(uuid.uuid4())

    async def app(scope, receive, send):
        await send({'type': 'http.response.start', 'status': 200, 'headers': []})
        await send({'type': 'http.response.body', 'body': b'customer-secret'})

    async def run():
        async def receive():
            return {'type': 'http.request', 'body': b'password=customer-secret'}
        async def send(_message):
            pass
        await telemetry.TelemetryMiddleware(app)({'type': 'http', 'path': '/agent/analyze', 'headers': [
            (b'traceparent', b'00-' + b'1' * 32 + b'-' + b'2' * 16 + b'-01'),
            (b'x-grafirio-query-id', query.encode())]}, receive, send)
    with patch.object(telemetry, '_exporter', handler):
        asyncio.run(run())
    event = handler.queue.get_nowait()
    assert event['queryId'] == uuid.UUID(query).hex
    assert event['traceId'] == '1' * 32
    assert event['spanId'] != '2' * 16
    assert event['parentSpanId'] == '2' * 16
    assert 'customer-secret' not in json.dumps(event)
    assert telemetry.correlation.get() == {}


def test_disabled_or_insecure_export_has_no_network_thread(monkeypatch):
    monkeypatch.setenv('TELEMETRY_ARCHIVE_ENDPOINT', 'http://collector.invalid/api/telemetry/ingest')
    monkeypatch.setenv('TELEMETRY_ARCHIVE_KEY', 'x' * 40)
    monkeypatch.delenv('TELEMETRY_ALLOW_INSECURE_TRANSPORT', raising=False)
    with patch.object(telemetry, '_exporter', None):
        telemetry.start_telemetry()
        assert telemetry._exporter is None


def test_malformed_configuration_does_not_prevent_engine_start(monkeypatch):
    monkeypatch.setenv('TELEMETRY_ARCHIVE_ENDPOINT', 'https://[invalid-ipv6')
    monkeypatch.setenv('TELEMETRY_ARCHIVE_KEY', 'x' * 40)
    with patch.object(telemetry, '_exporter', None):
        telemetry.start_telemetry()
        assert telemetry._exporter is None


def test_exporter_retries_same_event_id_after_collector_failure(monkeypatch):
    handler = telemetry.ArchiveHandler('https://collector.invalid', 'x' * 40)
    handler.event('python.job.started', queryId=str(uuid.uuid4()))
    attempts = []

    class Response:
        status = 200
        def read(self, _limit):
            return b'{"accepted":1,"inserted":1,"duplicates":0}'
        def __enter__(self):
            return self
        def __exit__(self, *args):
            pass

    class Opener:
        def open(self, request, timeout):
            attempts.append(json.loads(request.data))
            if len(attempts) == 1:
                raise OSError('synthetic collector outage')
            handler.stopping.set()
            return Response()
    monkeypatch.setattr(telemetry.urllib.request, 'build_opener', lambda *args: Opener())
    monkeypatch.setattr(handler.stopping, 'wait', lambda timeout: None)
    handler._run()
    assert len(attempts) == 2
    assert attempts[0]['events'][0]['id'] == attempts[1]['events'][0]['id']
    assert handler.failures == 1


def test_success_status_without_commit_ack_does_not_discard_pending_event(monkeypatch):
    import threading
    handler = telemetry.ArchiveHandler('https://collector.invalid', 'x' * 40)
    handler.event()
    ids = []

    class Response:
        status = 200
        def __enter__(self):
            return self
        def __exit__(self, *args):
            pass
        def read(self, _limit):
            return b'{"status":"ok"}' if len(ids) == 1 else b'{"accepted":1,"inserted":1,"duplicates":0}'

    class Opener:
        def open(self, request, timeout):
            ids.append(json.loads(request.data)['events'][0]['id'])
            if len(ids) == 2:
                handler.stopping.set()
            return Response()
    monkeypatch.setattr(telemetry.urllib.request, 'build_opener', lambda *args: Opener())
    monkeypatch.setattr(handler.stopping, 'wait', lambda timeout: None)
    deadline = threading.Timer(2, handler.stopping.set)
    deadline.start()
    try:
        handler._run()
    finally:
        deadline.cancel()
    assert len(ids) == 2 and ids[0] == ids[1]
    assert handler.failures == 1
