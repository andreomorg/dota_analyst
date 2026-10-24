"""Read-only, CPU audit using the exact Qwen3.5 training encoder."""
import argparse
import sys
from collections import Counter
from itertools import zip_longest
from pathlib import Path

from ..core.input_snapshot import capture_inputs, verify_inputs
from ..core.json import canonical
from ..core.reporting import write_report
from ..core.streams import iter_json_array, iter_jsonl
from ..core.validation import AuditError, require
from ..experiments.preparation import load_config
from ..hashing import file_hash
from ..tokenization import percentile
from .config import RUNTIME
from .encoding import encode_messages


def audit(experiment, snapshot, tokenizer=None):
    root, snapshot = Path(experiment), Path(snapshot)
    config_hash = file_hash(root / 'config.json')
    cfg = load_config(root)
    require(cfg.get('runtime') == RUNTIME and snapshot.name == cfg['revision'], 'Expected pinned Qwen3.5 runtime snapshot')
    files = {'config': root / 'config.json'}
    for split in ('development', 'validation'):
        files[split] = root / (split + '.jsonl')
        files[split + 'Metadata'] = root / (split + '-metadata.json')
    for name in ('tokenizer.json', 'tokenizer_config.json', 'chat_template.jinja'):
        files[name] = snapshot / name
    hashes = capture_inputs(files)
    require(hashes['config'] == config_hash, 'Audit config changed during reading')
    if tokenizer is None:
        from transformers import AutoTokenizer
        tokenizer = AutoTokenizer.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False)
    reports = {}
    for split, expected in (('development', cfg['samples']), ('validation', cfg['validationSamples'])):
        totals, targets, rules = Counter(), Counter(), Counter()
        failures, rows, encoded = [], 0, 0
        training_overflow = generation_overflow = 0
        sentinel = object()
        for index, (record, meta) in enumerate(zip_longest(iter_jsonl(files[split]),
                iter_json_array(files[split + 'Metadata']), fillvalue=sentinel)):
            require(record is not sentinel and meta is not sentinel, 'Dataset/metadata length mismatch: ' + split)
            require(type(meta) is dict and type(meta.get('row')) is int and meta['row'] == index,
                    'Metadata row mismatch: ' + split)
            rule = meta.get('policyRule')
            require(rule in {f'R{i}' for i in range(12)}, 'Unknown policy rule: ' + split)
            rules[rule] += 1
            rows += 1
            try:
                require(type(record) is dict, 'Expected SFT object')
                prefix, target = encode_messages(record.get('messages'), tokenizer)
            except AuditError as error:
                failures.append({'row': index, 'rule': rule, 'error': str(error)})
                continue
            total, response = len(prefix) + len(target), len(target)
            totals[total] += 1
            targets[response] += 1
            encoded += 1
            over_train, over_gen = total > cfg['maxTrainingTokens'], response > cfg['maxNewTokens']
            training_overflow += over_train
            generation_overflow += over_gen
            if over_train or over_gen:
                failures.append({'row': index, 'rule': rule, 'trainingTokens': total, 'targetTokens': response,
                                 'trainingOverflow': over_train, 'generationOverflow': over_gen})
        require(rows == expected, 'Configured sample count mismatch: ' + split)
        reports[split] = {'rows': rows, 'encodedRows': encoded, 'byRule': dict(sorted(rules.items())),
                         'missingRules': [f'R{i}' for i in range(12) if not rules[f'R{i}']],
                         'maxTrainingTokens': max(totals, default=None), 'p95TrainingTokens': percentile(totals, .95),
                         'maxTargetTokens': max(targets, default=None), 'p95TargetTokens': percentile(targets, .95),
                         'trainingOverflowExamples': training_overflow, 'generationOverflowExamples': generation_overflow,
                         'failures': failures}
    verify_inputs(files, hashes, 'Audit inputs changed during reading')
    return {'schemaVersion': 1, 'kind': 'qwen35-encoding-audit', 'runtime': RUNTIME, 'revision': cfg['revision'],
            'sourceFiles': hashes, 'datasets': reports, 'passed': all(not r['failures'] for r in reports.values()),
            'examplesTruncated': False, 'modelWeightsLoaded': False, 'trainingExecuted': False,
            'tacticalQualityApproved': False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--experiment', type=Path, required=True)
    parser.add_argument('--snapshot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        require(not args.output.exists(), 'Audit report must be new')
        result = audit(args.experiment, args.snapshot)
        write_report(args.output, result)
        print(canonical({'report': str(args.output), 'passed': result['passed']}))
        return 0 if result['passed'] else 2
    except (AuditError, OSError, ValueError, KeyError, TypeError) as error:
        print(canonical({'error': str(error), 'trainingExecuted': False}), file=sys.stderr)
        return 2
