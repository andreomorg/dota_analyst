"""Bounded JSON HTTP adapter with redirects disabled."""
import json
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener
from ..core.json import parse_json
from ..core.validation import AuditError, require
from .contracts import MAX_BODY, UpstreamError

class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class JsonHttpClient:
    def __init__(self, timeout):
        self.timeout = timeout
        self.opener = build_opener(NoRedirect())

    def fetch(self, url, payload=None, key='', limit=MAX_BODY, missing_context=False):
        headers = {'Accept': 'application/json'}
        if key:
            headers['Authorization'] = 'Bearer ' + key
        data = None if payload is None else json.dumps(payload, ensure_ascii=False).encode('utf-8')
        if data is not None:
            headers['Content-Type'] = 'application/json'
        try:
            with self.opener.open(Request(url, data=data, headers=headers), timeout=self.timeout) as response:
                raw = response.read(limit + 1)
            require(len(raw) <= limit, 'Upstream response exceeds limit')
            return parse_json(raw.decode('utf-8'))
        except HTTPError as exc:
            status = 404 if missing_context and exc.code == 404 else 502
            exc.close()
            raise UpstreamError(status=status) from exc
        except (TimeoutError, URLError, OSError) as exc:
            timeout = isinstance(exc, TimeoutError) or isinstance(getattr(exc, 'reason', None), TimeoutError)
            raise UpstreamError(status=504 if timeout else 502) from exc
        except (AuditError, UnicodeError) as exc:
            raise UpstreamError() from exc
