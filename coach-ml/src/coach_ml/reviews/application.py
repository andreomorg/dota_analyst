"""Review use cases depend on engine/fetch ports, not HTTP server implementations."""
from typing import Protocol
from ..core.validation import AuditError, require
from .contracts import UpstreamError, validate_request, validate_report

class ReviewEngine(Protocol):
    def review(self, value: dict) -> dict: ...

class JsonFetcher(Protocol):
    def __call__(self, url: str, payload=None, key='', limit=65536, missing_context=False): ...

class ReviewApplication:
    def __init__(self, settings, engine: ReviewEngine, fetch: JsonFetcher):
        self.settings = settings
        self.engine = engine
        self.fetch_context = fetch

    def by_match(self, value):
        require(isinstance(value, dict) and set(value) == {'matchId', 'heroId'}, 'Expected matchId and heroId')
        match, hero = value['matchId'], value['heroId']
        require(isinstance(match, str) and 1 <= len(match) <= 19 and match.isascii() and match.isdigit() and 0 < int(match) <= 2**63 - 1
                and type(hero) is int and 0 < hero <= 2**31 - 1, 'Invalid match or hero ID')
        require(self.settings.callback_url and self.settings.callback_key, 'C# callback is not configured')
        context = self.fetch_context(self.settings.callback_url + f'api/internal/coach/contexts/{match}/{hero}',
                             key=self.settings.callback_key, missing_context=True)
        try:
            require(isinstance(context, dict) and context.get('heroId') == hero, 'Callback returned another hero')
            return self.review(context)
        except UpstreamError:
            raise
        except (AuditError, ValueError, TypeError, KeyError) as exc:
            raise UpstreamError() from exc

    def review(self, value):
        context = validate_request(value)
        result = self.engine.review(context)
        require(isinstance(result, dict) and result.get('schemaVersion') == 1
                and isinstance(result.get('engine'), str) and bool(result['engine'])
                and result.get('humanReviewed') is False, 'Invalid review engine envelope')
        validate_report(context, result.get('report'))
        return result
