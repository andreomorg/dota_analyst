"""Bounded provisional content critique, retaining rejected and malformed cases."""
import argparse
from pathlib import Path

from ..core.json import canonical, digest, parse_json
from ..core.validation import require, AuditError
from .curation import CRITIC_SYSTEM, CALIBRATED_CRITIC_SYSTEM, validate_critique
from .pipeline import load_selection, ollama_generator, run_cases


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument('--index',type=Path)
    selection.add_argument('--bundle',type=Path)
    parser.add_argument('--predictions', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--ollama-url', default='http://dotaanalyst-tactical-ollama:11434/')
    parser.add_argument('--model', default='qwen3:8b')
    parser.add_argument('--include-support', action='store_true')
    parser.add_argument('--partition', choices=['development','validation-diagnostic'], default='development')
    parser.add_argument('--calibrated',action='store_true')
    parser.add_argument('--think',action='store_true')
    parser.add_argument('--max-tokens',type=int,default=1024)
    parser.add_argument('--actions',type=Path,help='Opt-in C# action receipt directory (development only)')
    parser.add_argument('--patches',type=Path,help='Opt-in audited development patch metadata directory')
    args = parser.parse_args(argv)
    require(128 <= args.max_tokens <= 4096, 'Critic token budget must be between 128 and 4096')
    cases = load_selection(args.source,args.index,args.bundle,args.include_support,args.partition,args.actions,args.patches)
    raw = args.predictions.read_bytes()
    require(raw.endswith(b'\n'), 'Incomplete source predictions')
    predictions = [parse_json(line.decode('utf-8')) for line in raw.splitlines()]
    require(0 < len(predictions) <= len(cases), 'Unexpected source prediction count')
    cases = cases[:len(predictions)]
    candidates = {}
    for (record, request), prediction in zip(cases, predictions):
        require(prediction['caseId'] == record['caseId'] and prediction['inputSha256'] == digest(request), 'Critique input mismatch')
        candidates[digest(request)] = prediction['candidate'] if prediction['candidate'] is not None else {
            'unstructuredResponse': prediction['raw']['text']}
    system = CALIBRATED_CRITIC_SYSTEM if args.calibrated else CRITIC_SYSTEM
    schema = None
    if args.calibrated:
        from .structured import critique_schema
        schema = lambda value:critique_schema(value['input'],value['candidate'])
    identity, generate = ollama_generator(args.ollama_url,args.model,args.max_tokens,system=system,schema_factory=schema,
                                          context=12288 if args.calibrated else 8192,thinking=args.think)
    identity.update({'purpose': 'provisional-content-critique',
                     'sourcePredictionsSha256': digest(predictions),
                     'generatorAndCriticMayShareModel': True, 'humanReviewed': False})

    def critique(request):
        return generate({'input': request, 'candidate': candidates[digest(request)]})

    def score(request, generated):
        try:
            require(generated.get('stopReason') != 'length', 'Critic token limit')
            review = parse_json(generated['text'])
            validate_critique(request, candidates[digest(request)], review)
            return {'valid': True, 'candidate': review, 'error': None}
        except (AuditError, KeyError, TypeError) as exc:
            return {'valid': False, 'candidate': None, 'error': str(exc)}

    result = run_cases(cases, args.output, identity, critique, scorer=score, system=system)
    rows = [parse_json(line) for line in (args.output/'predictions.jsonl').read_text().splitlines()]
    result.update({'purpose': 'provisional-content-critique', 'humanReviewed': False,
                   'verdicts': {key: sum(row['valid'] and row['candidate']['verdict'] == key for row in rows)
                                for key in ('reject', 'revise', 'provisional-accept')},
                   'generatorAndCriticMayShareModel': True, 'tacticalQualityApproved': False})
    (args.output/'content-review-summary.json').write_text(canonical(result)+'\n', encoding='utf-8')
    print(canonical(result))


if __name__ == '__main__':
    main()
