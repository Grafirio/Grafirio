"""Service authentication also protects docs, unknown routes and preflight requests."""

import hmac
import os

from starlette.responses import JSONResponse

KEY_HEADER = b"x-grafirio-internal-key"
MAX_REQUEST_BYTES = 2 * 1024 * 1024


class InternalAuthMiddleware:
    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        if scope["path"] == "/health" and scope["method"] == "GET":
            await self.app(scope, receive, send)
            return
        expected = os.getenv("INTERNAL_API_KEY", "")
        supplied = [value for name, value in scope["headers"] if name.lower() == KEY_HEADER]
        valid = hmac.compare_digest(supplied[0] if len(supplied) == 1 else b"", expected.encode("utf-8"))
        if not expected.strip() or not valid:
            response = JSONResponse({"detail": "Internal service authentication required."},
                                    status_code=401 if expected.strip() else 503)
            await response(scope, receive, send)
            return
        body = bytearray()
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            body.extend(message.get("body", b""))
            if len(body) > MAX_REQUEST_BYTES:
                await JSONResponse({"detail": "Request body too large."}, status_code=413)(scope, receive, send)
                return
            if not message.get("more_body", False):
                break
        delivered = False

        async def replay():
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": bytes(body), "more_body": False}
            return await receive()

        await self.app(scope, replay, send)