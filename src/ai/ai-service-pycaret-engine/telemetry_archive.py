"""Optional bounded export of safe logging metadata; never exports message/args/body/SQL."""
import contextvars
import datetime
import json
import logging
import os
import queue
import re
import threading
import time
import urllib.request
import uuid

correlation = contextvars.ContextVar('telemetry_correlation', default={})
_exporter = None


def valid_guid(value):
    try:
        return uuid.UUID(str(value)).hex
    except (ValueError, TypeError, AttributeError):
        return None


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class ArchiveHandler(logging.Handler):
    def __init__(self, endpoint, key, environment='Production', version='1.0.0', capacity=2000):
        super().__init__()
        self.endpoint, self.key = endpoint, key
        self.environment, self.version = environment, version
        self.instance = uuid.uuid4().hex
        self.queue = queue.Queue(maxsize=capacity)
        self.dropped = 0
        self.failures = 0
        self.stopping = threading.Event()
        self.thread = None

    def event(self, name='application.log', level='Information', **fields):
        event = {'id': uuid.uuid4().hex, 'timestamp': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                 'service': 'python', 'instanceId': self.instance, 'environment': self.environment,
                 'version': self.version, 'kind': 'log', 'name': name, 'level': level, **correlation.get()}
        # Explicit fields only: callers cannot add raw data by accident.
        for key in ('queryId', 'traceId', 'spanId', 'status', 'durationMs', 'exceptionType'):
            if key in fields:
                event[key] = fields[key]
        if 'queryId' in event:
            event['queryId'] = valid_guid(event['queryId'])
        try:
            self.queue.put_nowait(event)
        except queue.Full:
            self.dropped += 1

    def emit(self, record):
        level = 'Critical' if record.levelno >= 50 else 'Error' if record.levelno >= 40 else 'Warning' if record.levelno >= 30 else 'Information' if record.levelno >= 20 else 'Debug'
        self.event(level=level, exceptionType=record.exc_info[0].__name__ if record.exc_info else None)

    def start(self):
        self.thread = threading.Thread(target=self._run, name='telemetry-archive', daemon=True)
        self.thread.start()

    def _run(self):
        pending = []
        opener = urllib.request.build_opener(NoRedirect())
        while not self.stopping.is_set():
            if not pending:
                try:
                    pending.append(self.queue.get(timeout=0.5))
                except queue.Empty:
                    continue
            while len(pending) < 100:
                try:
                    pending.append(self.queue.get_nowait())
                except queue.Empty:
                    break
            try:
                request = urllib.request.Request(self.endpoint, data=json.dumps({'events': pending, 'dropped': self.dropped}).encode(),
                    headers={'Content-Type': 'application/json', 'X-Grafirio-Telemetry-Key': self.key}, method='POST')
                with opener.open(request, timeout=5) as response:
                    if response.status != 200:
                        raise RuntimeError('Telemetry rejected')
                    ack = json.loads(response.read(4096))
                    if (any(type(ack.get(key)) is not int for key in ('accepted', 'inserted', 'duplicates'))
                            or ack['accepted'] != len(pending) or min(ack['inserted'], ack['duplicates']) < 0
                            or ack['inserted'] + ack['duplicates'] != len(pending)):
                        raise RuntimeError('Invalid telemetry acknowledgement')
                pending.clear()
            except Exception as error:
                self.failures += 1
                # A malformed batch must not poison the exporter forever.
                if getattr(error, 'code', 0) in (400, 413):
                    self.dropped += len(pending)
                    pending.clear()
                self.stopping.wait(5)

    def stop(self):
        self.stopping.set()
        if self.thread:
            self.thread.join(timeout=6)


def start_telemetry():
    global _exporter
    endpoint, key = os.getenv('TELEMETRY_ARCHIVE_ENDPOINT', ''), os.getenv('TELEMETRY_ARCHIVE_KEY', '')
    from urllib.parse import urlparse
    try:
        parsed = urlparse(endpoint)
        valid_url = parsed.hostname and not parsed.username and not parsed.password and not parsed.query and not parsed.fragment
    except ValueError:
        return
    if len(key) < 32 or not all(33 <= ord(character) <= 126 for character in key) or not valid_url:
        return
    if parsed.scheme != 'https' and not (parsed.scheme == 'http' and os.getenv('TELEMETRY_ALLOW_INSECURE_TRANSPORT') == 'true'):
        return
    if _exporter is not None:
        return
    _exporter = ArchiveHandler(endpoint, key, os.getenv('ENVIRONMENT', 'Production'), os.getenv('GRAFIRIO_BUILD_SHA', '1.0.0'))
    logging.getLogger().addHandler(_exporter)
    _exporter.start()


def stop_telemetry():
    global _exporter
    if _exporter:
        logging.getLogger().removeHandler(_exporter)
        _exporter.stop()
        _exporter = None


def telemetry_event(name, **fields):
    if _exporter:
        _exporter.event(name, **fields)


class TelemetryMiddleware:
    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope['type'] != 'http' or scope['path'] == '/health':
            return await self.app(scope, receive, send)
        headers = dict(scope.get('headers', []))
        trace = headers.get(b'traceparent', b'').decode('ascii', errors='ignore')
        match = re.fullmatch(r'00-([0-9a-f]{32})-([0-9a-f]{16})-[0-9a-f]{2}', trace)
        context = {'queryId': valid_guid(headers.get(b'x-grafirio-query-id', b'').decode('ascii', errors='ignore'))}
        if match:
            context['traceId'], context['parentSpanId'] = match.groups()
            context['spanId'] = uuid.uuid4().hex[:16]
        token = correlation.set(context)
        started, status = time.monotonic(), 500

        async def capture(message):
            nonlocal status
            if message['type'] == 'http.response.start':
                status = message['status']
            await send(message)
        try:
            await self.app(scope, receive, capture)
        finally:
            telemetry_event('http.request', level='Error' if status >= 500 else 'Information',
                            durationMs=(time.monotonic() - started) * 1000, status='failed' if status >= 400 else 'completed')
            correlation.reset(token)
