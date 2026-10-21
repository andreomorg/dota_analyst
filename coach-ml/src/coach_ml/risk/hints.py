"""Candidate tips from the next-region model, for review by hand (not for the site yet). Doing something else than
high-level players is not a mistake by itself, so a moment becomes a candidate only when all three hold: the player's
destination was unlikely, the model was confident about another one, and something bad followed (death, or far less
net worth than usual in the next minute). If the rule holds up in review, it moves to the C# Domain."""
import argparse
import json
import sys
from pathlib import Path

from ..core.json import parse_json
from ..core.validation import AuditError, require
from ..hashing import file_hash
from .data import DESTINATIONS, FINAL_TEST, dumps, export_identity, load
from .next_region import KIND
from .train import ml

UNLIKELY = 0.15
CONFIDENT = 0.50
GAIN_SECONDS = 60
# Less than this share of the usual net-worth gain (same role, same 5 minutes) counts as a bad minute.
LOW_GAIN_SHARE = 0.5
WINDOW_SECONDS = 300
DEAD = DESTINATIONS.index('dead')


def candidates(probabilities, labels, groups, extra, net_worth, focus, np):
    """The moments of the focus heroes that pass the three conditions, in time order."""
    keys = {(int(match), int(hero), int(second)): index
            for index, (match, hero, second) in enumerate(zip(groups, extra['heroId'], extra['seconds'], strict=False))}
    # The net worth gained in the next minute, where the same hero has a sample a minute later (alive then).
    gains = np.full(len(labels), np.nan)
    for (match, hero, second), index in keys.items():
        if (later := keys.get((match, hero, second + GAIN_SECONDS))) is not None:
            gains[index] = net_worth[later] - net_worth[index]
    roles = np.asarray([role or 'none' for role in extra['role']])
    windows = extra['seconds'] // WINDOW_SECONDS
    usual = {}
    found = []
    for match_id, hero_id in focus:
        rows = np.flatnonzero((groups == match_id) & (extra['heroId'] == hero_id))
        for index in sorted(rows, key=lambda row: extra['seconds'][row]):
            went, best = int(labels[index]), int(np.argmax(probabilities[index]))
            if probabilities[index, went] >= UNLIKELY or probabilities[index, best] <= CONFIDENT or best == went:
                continue
            key = (roles[index], windows[index])
            if key not in usual:
                same = (roles == key[0]) & (windows == key[1]) & ~np.isnan(gains)
                usual[key] = float(np.median(gains[same])) if same.any() else None
            died = went == DEAD
            low_gain = (not np.isnan(gains[index]) and usual[key] is not None and usual[key] > 0
                        and gains[index] < LOW_GAIN_SHARE * usual[key])
            if not (died or low_gain):
                continue
            found.append({
                'matchId': str(match_id), 'heroId': int(hero_id), 'seconds': int(extra['seconds'][index]),
                'went': DESTINATIONS[went], 'wentProbability': round(float(probabilities[index, went]), 3),
                'usualDestination': DESTINATIONS[best], 'usualProbability': round(float(probabilities[index, best]), 3),
                'consequence': 'died' if died else 'lowNetWorthGain',
                'netWorthGain': None if np.isnan(gains[index]) else int(gains[index]),
                'usualNetWorthGain': None if usual[key] is None else round(usual[key]),
            })
    return found


def hints(models_dir, export, output, partitions, focus, lib=None):
    lib = lib or ml()
    np = lib['np']
    models_dir, output = Path(models_dir), Path(output)
    require(not output.exists(), f'Output already exists: {output}')
    require(FINAL_TEST not in partitions and not any(split.startswith('reserved-') for split in partitions),
            'Hints never open the final test or the reserved partitions')
    training = parse_json((models_dir / 'training.json').read_text(encoding='utf-8'))
    require(training.get('kind') == KIND, 'These are not next-region models')
    require(file_hash(models_dir / 'models.joblib') == training['modelsSha256'], 'models.joblib changed after training')
    require(export_identity(export) == training['export'], 'The export differs from the one the models were trained on')
    columns = training['columns']
    require('netWorth' in columns, 'The models need the netWorth column')
    model = lib['joblib'].load(models_dir / 'models.joblib')['boosted']
    features, labels, groups, extra = load(export, partitions, columns, np, kind=KIND)
    probabilities = np.asarray(model.predict_proba(features), dtype=float)
    found = candidates(probabilities, labels, groups, extra, features[:, columns.index('netWorth')], focus, np)
    report = {'schemaVersion': 1, 'partitions': list(partitions),
              'rule': {'unlikely': UNLIKELY, 'confident': CONFIDENT, 'gainSeconds': GAIN_SECONDS, 'lowGainShare': LOW_GAIN_SHARE},
              'candidates': found, 'humanReviewed': False}
    output.mkdir(parents=True)
    with (output / 'hints.json').open('x', encoding='utf-8') as stream:
        stream.write(dumps(report))
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml region-hints')
    parser.add_argument('--models', required=True)
    parser.add_argument('--export', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--partition', action='append', required=True)
    parser.add_argument('--focus', required=True, help='JSON file: [{"matchId": "...", "heroId": 1}, ...]')
    args = parser.parse_args(argv)
    try:
        focus = [(int(item['matchId']), int(item['heroId']))
                 for item in parse_json(Path(args.focus).read_text(encoding='utf-8-sig'))]
        report = hints(args.models, args.export, args.output, args.partition, focus)
    except (AuditError, OSError, KeyError, ValueError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps({'candidates': len(report['candidates'])}))
    return 0
