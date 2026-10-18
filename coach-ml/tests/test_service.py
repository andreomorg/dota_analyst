import copy
from concurrent.futures import ThreadPoolExecutor
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import threading
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from coach_ml.audit import AuditError
from coach_ml.service import ReviewService, Settings, UpstreamError, create_server, validate_report, validate_request

KEY = 'machine-test-key-with-enough-entropy'


def context():
    return {'schemaVersion': 1, 'heroId': 1, 'role': {'position': 1}, 'scenes': [
        {'episodeId': 'death', 'anchorSeconds': 30, 'missingHeroPositions': 10,
         'evidence': [{'id': 'outcome', 'kind': 'retrospective', 'seconds': 40, 'description': 'Retrospective death.'}]}]}


class ServiceTests(unittest.TestCase):
    def setUp(self):
        self.callback_calls = []
        self.callback_status = 200
        self.callback_body = None
        self.callback_location = ''
        outer = self

        class Callback(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def do_GET(self):
                outer.callback_calls.append((self.path, self.headers.get('Authorization')))
                raw = outer.callback_body if outer.callback_body is not None else json.dumps(context()).encode()
                self.send_response(outer.callback_status)
                if outer.callback_location:
                    self.send_header('Location', outer.callback_location)
                self.send_header('Content-Length', str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

        self.callback = ThreadingHTTPServer(('127.0.0.1', 0), Callback)
        self.start(self.callback)
        settings = Settings(KEY, f'http://127.0.0.1:{self.callback.server_port}/', KEY)
        self.server = create_server(settings, ('127.0.0.1', 0))
        self.start(self.server)
        self.base = f'http://127.0.0.1:{self.server.server_port}'

    def start(self, server):
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)

    def post(self, path, payload, key=KEY, headers=None):
        request = Request(self.base + path, data=json.dumps(payload).encode(), headers={
            'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json', **(headers or {})})
        with urlopen(request, timeout=5) as response:
            return json.load(response)

    def test_health_does_not_claim_model_readiness(self):
        with urlopen(self.base + '/health', timeout=5) as response:
            self.assertEqual(json.load(response)['service'], 'coach-ml')

    def test_authentication_is_required_before_parsing_or_callback(self):
        with self.assertRaises(HTTPError) as error:
            self.post('/v1/reviews/by-match', {'matchId': '1', 'heroId': 1}, key='wrong')
        self.assertEqual(error.exception.code, 401)
        self.assertEqual(self.callback_calls, [])

    def test_review_by_context_identifies_policy_preview_and_preserves_references(self):
        result = self.post('/v1/reviews', context())
        self.assertEqual(result['engine'], 'policy-preview-v1')
        self.assertFalse(result['humanReviewed'])
        self.assertEqual(result['report']['assessments'][0]['evidenceIds'], ['outcome'])
        self.assertEqual(result['report']['assessments'][0]['disposition'], 'insufficient')

    def test_python_fetches_context_from_csharp_with_shared_authentication(self):
        result = self.post('/v1/reviews/by-match', {'matchId': '123', 'heroId': 1})
        self.assertEqual(result['schemaVersion'], 1)
        self.assertEqual(self.callback_calls, [('/api/internal/coach/contexts/123/1', 'Bearer ' + KEY)])

    def test_client_cannot_supply_a_callback_url_or_path_traversal(self):
        for payload in [{'matchId': '../secret', 'heroId': 1},
                        {'matchId': '1', 'heroId': 1, 'callbackUrl': 'http://example.invalid/'}]:
            with self.subTest(payload=payload), self.assertRaises(HTTPError) as error:
                self.post('/v1/reviews/by-match', payload)
            self.assertEqual(error.exception.code, 422)
        self.assertFalse(self.callback_calls)

    def test_oversized_request_is_rejected(self):
        with self.assertRaises(HTTPError) as error:
            self.post('/v1/reviews', context(), headers={'Content-Length': '65537'})
        self.assertEqual(error.exception.code, 413)

    def test_future_evidence_and_duplicate_scenes_are_rejected(self):
        request = context()
        request['scenes'][0]['evidence'].append({'id': 'economy', 'kind': 'earlier-snapshot', 'seconds': 31, 'description': 'Future snapshot'})
        with self.assertRaisesRegex(AuditError, 'Future'):
            validate_request(request)
        request = context()
        request['scenes'].append(copy.deepcopy(request['scenes'][0]))
        with self.assertRaisesRegex(AuditError, 'Duplicate'):
            validate_request(request)

    def test_truncated_ollama_output_is_rejected_in_python(self):
        service = ReviewService(Settings(KEY, engine='ollama'))
        service.fetch = lambda *_args, **_kwargs: {'done': True, 'done_reason': 'length', 'response': '{}'}
        with self.assertRaisesRegex(AuditError, 'Incomplete'):
            service.review(context())

    def test_unknown_model_references_and_duplicate_keys_are_rejected(self):
        service = ReviewService(Settings(KEY))
        report = service.review(context())['report']
        report['assessments'][0]['evidenceIds'] = ['invented']
        service = ReviewService(Settings(KEY, engine='ollama'))
        service.fetch = lambda *_args, **_kwargs: {'done': True, 'response': json.dumps(report)}
        with self.assertRaisesRegex(AuditError, 'reference'):
            service.review(context())
        service.fetch = lambda *_args, **_kwargs: {'done': True, 'response': '{"assessments":[],"assessments":[]}'}
        with self.assertRaisesRegex(AuditError, 'Duplicate'):
            service.review(context())

    def test_absurd_content_length_and_wrong_media_type_return_controlled_errors(self):
        for headers, status in [({'Content-Length': '9' * 5000}, 413),
                                ({'Content-Type': 'text/plain'}, 415)]:
            with self.subTest(status=status), self.assertRaises(HTTPError) as error:
                self.post('/v1/reviews', context(), headers=headers)
            self.assertEqual(error.exception.code, status)
        self.assertEqual(self.post('/v1/reviews', context())['schemaVersion'], 1)

    def test_missing_archived_context_returns_404_instead_of_invalid_client_contract(self):
        self.callback_status = 404
        with self.assertRaises(HTTPError) as error:
            self.post('/v1/reviews/by-match', {'matchId': '123', 'heroId': 1})
        self.assertEqual(error.exception.code, 404)

    def test_malformed_or_wrong_hero_callback_is_an_upstream_error(self):
        for raw in [b'not json', json.dumps({**context(), 'heroId': 2}).encode(), b'x' * 65537]:
            with self.subTest(size=len(raw)):
                self.callback_body = raw
                with self.assertRaises(HTTPError) as error:
                    self.post('/v1/reviews/by-match', {'matchId': '123', 'heroId': 1})
                self.assertEqual(error.exception.code, 502)

    def test_redirect_is_not_followed_and_machine_key_never_reaches_redirect_target(self):
        self.callback_status = 302
        self.callback_location = self.base + '/secret-target'
        with self.assertRaises(HTTPError) as error:
            self.post('/v1/reviews/by-match', {'matchId': '123', 'heroId': 1})
        self.assertEqual(error.exception.code, 502)
        self.assertEqual(len(self.callback_calls), 1)

    def test_model_contract_failure_is_an_upstream_error(self):
        with patch('coach_ml.service.ReviewService.review', side_effect=UpstreamError('private response')):
            with self.assertRaises(HTTPError) as error:
                self.post('/v1/reviews', context())
        self.assertEqual(error.exception.code, 502)
        self.assertNotIn(b'private', error.exception.read())

    def test_busy_review_returns_retry_after_while_health_remains_available(self):
        entered, release = threading.Event(), threading.Event()
        original = ReviewService.review

        def wait_for_release(service, value):
            entered.set()
            if not release.wait(5):
                raise RuntimeError('Test did not release review')
            return original(service, value)

        with patch('coach_ml.service.ReviewService.review', wait_for_release), ThreadPoolExecutor(1) as worker:
            pending = worker.submit(self.post, '/v1/reviews', context())
            try:
                self.assertTrue(entered.wait(3))
                with self.assertRaises(HTTPError) as error:
                    self.post('/v1/reviews', context())
                self.assertEqual(error.exception.code, 503)
                self.assertEqual(error.exception.headers['Retry-After'], '1')
                with urlopen(self.base + '/health', timeout=3) as response:
                    self.assertEqual(response.status, 200)
            finally:
                release.set()
            self.assertEqual(pending.result(timeout=3)['schemaVersion'], 1)
        self.assertEqual(self.post('/v1/reviews', context())['schemaVersion'], 1)

    def test_timeout_setting_rejects_boolean_nan_and_infinity(self):
        for value in [True, float('nan'), float('inf'), 0]:
            with self.subTest(value=value), self.assertRaises(AuditError):
                Settings(KEY, timeout=value).validate()

    def test_upstream_timeout_is_distinct_and_releases_review_slot(self):
        service = ReviewService(Settings(KEY))
        with patch.object(service.opener, 'open', side_effect=TimeoutError):
            with self.assertRaises(UpstreamError) as error:
                service.fetch('http://127.0.0.1/')
        self.assertEqual(error.exception.status, 504)
        with patch('coach_ml.service.ReviewService.fetch', side_effect=UpstreamError(status=504)):
            with self.assertRaises(HTTPError) as error:
                self.post('/v1/reviews/by-match', {'matchId': '123', 'heroId': 1})
            self.assertEqual(error.exception.code, 504)
        self.assertEqual(self.post('/v1/reviews', context())['schemaVersion'], 1)

    def test_preview_upgrade_does_not_assume_a_castable_spell(self):
        value = context()
        value['scenes'][0]['evidence'].append({'id': 'upgrade-0', 'kind': 'ability-upgrade',
            'seconds': 25, 'description': 'special_bonus_unique_nyx'})
        result = self.post('/v1/reviews', value)
        self.assertIn('ativa, passiva ou talento', result['report']['assessments'][0]['exercise'])

    def test_long_assessment_is_not_accepted_by_trimming_whitespace(self):
        report = ReviewService(Settings(KEY)).review(context())['report']
        report['assessments'][0]['exercise'] = 'x' + ' ' * 700
        with self.assertRaisesRegex(AuditError, 'text'):
            validate_report(context(), report)


if __name__ == '__main__':
    unittest.main()
