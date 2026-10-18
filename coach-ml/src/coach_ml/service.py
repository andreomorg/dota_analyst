"""HTTP transport and composition root for the internal CPU review service."""
import argparse
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import threading
from urllib.error import HTTPError, URLError
from .core.json import parse_json
from .core.validation import AuditError, require
from .reviews.contracts import MAX_BODY, Settings, UpstreamError, validate_report, validate_request
from .reviews.upstream import JsonHttpClient, NoRedirect
from .reviews.engines import ConfiguredReviewEngine, INSTRUCTIONS
from .reviews.application import ReviewApplication

class ReviewService(ReviewApplication):
    def __init__(self, settings, *, engine=None, client=None):
        settings.validate()
        self.client = client if client is not None else JsonHttpClient(settings.timeout)
        self.opener = self.client.opener
        self.busy = threading.BoundedSemaphore(1)
        # Dispatch through self.fetch so compatibility overrides and injected clients remain effective.
        super().__init__(settings, engine if engine is not None else ConfiguredReviewEngine(settings, self._fetch), self._fetch)

    def _fetch(self, *args, **kwargs):
        return self.fetch(*args, **kwargs)

    def fetch(self, *args, **kwargs):
        return self.client.fetch(*args, **kwargs)

def create_server(settings, address=('0.0.0.0', 8090)):
    service = ReviewService(settings)

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass  # Never log authorization headers, private context or upstream payloads.

        def reply(self, status, value):
            raw = json.dumps(value, ensure_ascii=False).encode('utf-8')
            self.send_response(status)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Content-Length', str(len(raw)))
            if status == 503:
                self.send_header('Retry-After', '1')
            self.end_headers()
            self.wfile.write(raw)

        def do_GET(self):
            if self.path == '/health':
                self.reply(200, {'status': 'ok', 'service': 'coach-ml', 'schemaVersion': 1})
            else:
                self.reply(404, {'error': 'not-found'})

        def do_POST(self):
            self.connection.settimeout(10)
            expected = 'Bearer ' + service.settings.api_key
            if not hmac.compare_digest(self.headers.get('Authorization', '').encode(), expected.encode()):
                self.reply(401, {'error': 'unauthorized'})
                return
            if self.path not in ['/v1/reviews', '/v1/reviews/by-match']:
                self.reply(404, {'error': 'not-found'})
                return
            lengths = self.headers.get_all('Content-Length', [])
            length = lengths[0] if len(lengths) == 1 else ''
            if not 1 <= len(length) <= 5 or not length.isascii() or not length.isdigit() or not 0 < int(length) <= MAX_BODY or self.headers.get('Transfer-Encoding'):
                self.reply(413, {'error': 'invalid-body-size'})
                return
            if self.headers.get_content_type() != 'application/json':
                self.reply(415, {'error': 'unsupported-media-type'})
                return
            if not service.busy.acquire(blocking=False):
                self.reply(503, {'error': 'busy'})
                return
            try:
                try:
                    raw = self.rfile.read(int(length))
                except TimeoutError:
                    self.reply(408, {'error': 'request-timeout'})
                    return
                require(len(raw) == int(length), 'Incomplete request body')
                value = parse_json(raw.decode('utf-8'))
                result = service.review(value) if self.path == '/v1/reviews' else service.by_match(value)
                self.reply(200, result)
            except UpstreamError as exc:
                self.reply(exc.status, {'error': 'context-not-found' if exc.status == 404 else
                    'upstream-timeout' if exc.status == 504 else 'upstream-unavailable'})
            except (AuditError, ValueError, UnicodeError, TypeError, KeyError):
                self.reply(422, {'error': 'invalid-review-contract'})
            except (HTTPError, URLError, TimeoutError, OSError):
                self.reply(502, {'error': 'upstream-unavailable'})
            finally:
                service.busy.release()

    return ThreadingHTTPServer(address, Handler)


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml serve')
    parser.add_argument('--host', default='0.0.0.0')
    parser.add_argument('--port', type=int, default=8090)
    args = parser.parse_args(argv)
    settings = Settings(api_key=os.environ.get('COACH_ML_API_KEY', ''),
                        callback_url=os.environ.get('COACH_ML_CALLBACK_URL', ''),
                        callback_key=os.environ.get('COACH_ML_CALLBACK_KEY', ''),
                        engine=os.environ.get('COACH_ML_ENGINE', 'policy-preview'),
                        ollama_url=os.environ.get('COACH_ML_OLLAMA_URL', 'http://ollama:11434/'),
                        model=os.environ.get('COACH_ML_MODEL', 'qwen3:8b'),
                        timeout=float(os.environ.get('COACH_ML_TIMEOUT_SECONDS', '180')))
    with create_server(settings, (args.host, args.port)) as server:
        server.serve_forever()
