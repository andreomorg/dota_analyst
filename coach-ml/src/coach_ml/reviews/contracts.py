"""Strict review DTO validation; no network or ML dependencies."""
from dataclasses import dataclass
import math
from urllib.parse import urlsplit
from ..core.validation import AuditError, require

MAX_BODY = 65536

class UpstreamError(AuditError):
    def __init__(self, message='Invalid upstream response', status=502):
        super().__init__(message)
        self.status = status


@dataclass(frozen=True)
class Settings:
    api_key: str
    callback_url: str = ''
    callback_key: str = ''
    engine: str = 'policy-preview'
    ollama_url: str = 'http://ollama:11434/'
    model: str = 'qwen3:8b'
    timeout: float = 180

    def validate(self):
        require(len(self.api_key) >= 16, 'COACH_ML_API_KEY must contain at least 16 characters')
        require(self.engine in ['policy-preview', 'ollama'], 'Unsupported review engine')
        require(type(self.timeout) in [int, float] and math.isfinite(self.timeout)
                and 0 < self.timeout <= 600, 'Invalid upstream timeout')
        require(not self.callback_url or len(self.callback_key) >= 16, 'Callback key must contain at least 16 characters')
        for address in [self.callback_url, self.ollama_url]:
            if address:
                parsed = urlsplit(address)
                require(parsed.scheme in ['http', 'https'] and parsed.hostname and not parsed.username
                        and not parsed.password and not parsed.query and not parsed.fragment and address.endswith('/'),
                        'Expected an absolute http(s) base URL ending with /')


def validate_request(value):
    require(isinstance(value, dict) and type(value.get('schemaVersion')) is int and value['schemaVersion'] == 1,
            'Expected review schemaVersion 1')
    require(type(value.get('heroId')) is int and value['heroId'] > 0, 'Invalid heroId')
    role = value.get('role')
    require(isinstance(role, dict) and type(role.get('position')) is int and 1 <= role['position'] <= 5, 'Invalid role')
    scenes = value.get('scenes')
    require(isinstance(scenes, list) and len(scenes) <= 3, 'Expected up to three scenes')
    seen = set()
    for scene in scenes:
        require(isinstance(scene, dict) and isinstance(scene.get('episodeId'), str) and 0 < len(scene['episodeId']) <= 200,
                'Invalid scene ID')
        require(scene['episodeId'] not in seen, 'Duplicate scene ID')
        seen.add(scene['episodeId'])
        anchor = scene.get('anchorSeconds')
        require(type(anchor) is int and anchor >= 0, 'Invalid scene anchor')
        evidence = scene.get('evidence')
        require(isinstance(evidence, list) and 1 <= len(evidence) <= 24, 'Invalid evidence count')
        refs = set()
        outcomes = 0
        for item in evidence:
            require(isinstance(item, dict) and isinstance(item.get('id'), str) and item['id']
                    and item['id'] not in refs, 'Duplicate or invalid evidence ID')
            refs.add(item['id'])
            kind, seconds = item.get('kind'), item.get('seconds')
            require(kind in ['retrospective', 'omniscient-position', 'earlier-snapshot', 'purchase', 'ability-upgrade', 'objective']
                    and type(seconds) is int, 'Invalid evidence kind or time')
            require(isinstance(item.get('description'), str) and 0 < len(item['description']) <= 2000, 'Invalid evidence description')
            if kind == 'retrospective':
                require(item['id'] == 'outcome' and seconds >= anchor, 'Invalid retrospective outcome')
                outcomes += 1
            else:
                require(seconds <= anchor, 'Future evidence in earlier snapshot')
        require(outcomes == 1, 'Expected exactly one retrospective outcome')
    return value


def validate_report(context, report):
    require(isinstance(report, dict) and set(report) == {'assessments'} and isinstance(report['assessments'], list), 'Invalid review report')
    scenes = {s['episodeId']: s for s in context['scenes']}
    require(len(report['assessments']) == len(scenes), 'Missing assessments')
    used = set()
    for row in report['assessments']:
        require(isinstance(row, dict) and set(row) == {'episodeId', 'disposition', 'evidenceIds', 'hypothesis', 'alternative', 'exercise', 'limitations'}, 'Invalid assessment fields')
        episode = row['episodeId']
        require(isinstance(episode, str) and episode in scenes and episode not in used, 'Unknown or duplicate assessment scene')
        used.add(episode)
        require(row['disposition'] in ['review', 'insufficient'], 'Invalid assessment disposition')
        refs = row['evidenceIds']
        require(isinstance(refs, list) and 1 <= len(refs) <= 8 and all(isinstance(r, str) for r in refs)
                and len(set(refs)) == len(refs) and all(r in {e['id'] for e in scenes[episode]['evidence']} for r in refs), 'Unknown evidence reference')
        require(all(isinstance(row[field], str) and 0 < len(row[field].strip()) and len(row[field]) <= 700
                    for field in ['hypothesis', 'alternative', 'exercise', 'limitations']), 'Invalid assessment text')
    return report
