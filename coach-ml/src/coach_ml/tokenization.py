"""Measure training and generation budgets without loading model weights."""
import argparse
from collections import Counter
import json
from pathlib import Path
import re
import sys

from .audit import AuditError, iter_jsonl, require


def percentile(counts, fraction):
    total = sum(counts.values())
    threshold = max(1, int(total * fraction + 0.999999))
    seen = 0
    for length, count in sorted(counts.items()):
        seen += count
        if seen >= threshold:
            return length


def audit_tokens(datasets, snapshot, max_training_tokens, max_new_tokens, tokenizer=None):
    snapshot = Path(snapshot)
    require(snapshot.is_dir() and re.fullmatch(r'[0-9a-f]{40}', snapshot.name),
            'Expected an existing local snapshot directory pinned by model commit')
    require(type(max_training_tokens) is int and 1 <= max_training_tokens <= 32768
            and type(max_new_tokens) is int and 1 <= max_new_tokens <= 8192, 'Invalid token budget')
    require(bool(datasets), 'Expected at least one dataset')
    if tokenizer is None:
        try:
            from transformers import AutoTokenizer
        except ImportError as exc:
            raise AuditError('Tokenizer dependencies unavailable; use the training environment') from exc
        tokenizer = AutoTokenizer.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False)
    require(isinstance(tokenizer.eos_token, str) and bool(tokenizer.eos_token), 'Tokenizer has no EOS token')
    reports = []
    for file in datasets:
        totals, prefixes, targets = Counter(), Counter(), Counter()
        overflow_training, overflow_generation, rows = 0, 0, 0
        longest = []
        for index, record in enumerate(iter_jsonl(file)):
            messages = record.get('messages') if isinstance(record, dict) else None
            require(isinstance(messages, list) and len(messages) == 3
                    and all(isinstance(m, dict) and isinstance(m.get('content'), str) and m['content'].strip() for m in messages)
                    and [m.get('role') for m in messages] == ['system', 'user', 'assistant'],
                    f'Invalid training messages at row {index}')
            prompt = tokenizer.apply_chat_template(messages[:2], tokenize=False,
                add_generation_prompt=True, enable_thinking=False)
            prefix = len(tokenizer(prompt, add_special_tokens=False)['input_ids'])
            target = len(tokenizer(messages[2]['content'] + tokenizer.eos_token, add_special_tokens=False)['input_ids'])
            require(prefix > 0 and target > 0, 'Empty tokenized example')
            total = prefix + target
            totals[total] += 1
            prefixes[prefix] += 1
            targets[target] += 1
            overflow_training += int(total > max_training_tokens)
            overflow_generation += int(target > max_new_tokens)
            rows += 1
            longest.append({'row': index, 'trainingTokens': total, 'targetTokens': target})
            longest = sorted(longest, key=lambda r: (-r['trainingTokens'], r['row']))[:5]
        require(rows > 0, 'Empty tokenization dataset')
        reports.append({'dataset': Path(file).name, 'rows': rows, 'maxTrainingTokens': max(totals),
            'p95TrainingTokens': percentile(totals, 0.95), 'maxPromptTokens': max(prefixes),
            'maxTargetTokens': max(targets), 'p95TargetTokens': percentile(targets, 0.95),
            'trainingOverflowExamples': overflow_training, 'generationOverflowExamples': overflow_generation,
            'longestExamples': longest})
    return {'schemaVersion': 1, 'kind': 'token-budget-audit', 'revision': snapshot.name,
        'trainingBudget': max_training_tokens, 'generationBudget': max_new_tokens, 'datasets': reports,
        'tokenBudgetsPassed': not any(r['trainingOverflowExamples'] or r['generationOverflowExamples'] for r in reports),
        'modelWeightsLoaded': False, 'examplesTruncated': False, 'trainingExecuted': False,
        'tacticalQualityApproved': False}


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml token-budget', description=__doc__)
    parser.add_argument('--dataset', required=True, type=Path, action='append')
    parser.add_argument('--snapshot', required=True, type=Path)
    parser.add_argument('--max-training-tokens', required=True, type=int)
    parser.add_argument('--max-new-tokens', required=True, type=int)
    args = parser.parse_args(argv)
    try:
        result = audit_tokens(args.dataset, args.snapshot, args.max_training_tokens, args.max_new_tokens)
        print(json.dumps(result, indent=2))
        return 0 if result['tokenBudgetsPassed'] else 2
    except (AuditError, OSError, ValueError, TypeError, KeyError) as exc:
        print(json.dumps({'error': str(exc), 'trainingExecuted': False}), file=sys.stderr)
        return 2
