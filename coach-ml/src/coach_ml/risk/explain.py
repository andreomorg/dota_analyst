"""Explain the visible death-risk model: which numbers matter overall, and what stood out before each death."""
import argparse
import json
import sys
from pathlib import Path

from ..core.json import parse_json
from ..core.validation import AuditError, require
from ..hashing import file_hash
from .data import CATEGORICAL, FINAL_TEST, REGIONS, dumps, export_identity, load
from .evaluate import PHASES
from .train import SEED, ml

MODEL = 'boostedVisible'
IMPORTANCE_ROWS = 200_000
IMPORTANCE_REPEATS = 3
DEPENDENCE_ROWS = 20_000
DEPENDENCE_POINTS = 10
TOP = 5
STANDOUTS = 3
REFERENCE_WINDOW_SECONDS = 300
# Explanations are about the player's own situation; the clock and the map coordinates explain nothing to act on.
NOT_ACTIONABLE = {'seconds', 'x', 'y', 'team', 'role'}


def pr_auc(labels, predicted):
    from sklearn.metrics import average_precision_score
    return float(average_precision_score(labels, predicted))


def importance(model, features, labels, columns, np, rng):
    """Permutation importance: how much PR-AUC falls when one column is shuffled (more = the model leans on it)."""
    base = pr_auc(labels, model.predict(features))
    result = {}
    for index, column in enumerate(columns):
        drops = []
        for _ in range(IMPORTANCE_REPEATS):
            shuffled = features.copy()
            shuffled[:, index] = rng.permutation(shuffled[:, index])
            drops.append(base - pr_auc(labels, model.predict(shuffled)))
        result[column] = round(float(np.mean(drops)), 5)
    # A list: the report's JSON sorts object keys, which would lose the ranking.
    return [{'column': column, 'prAucDrop': drop} for column, drop in sorted(result.items(), key=lambda item: -item[1])]


def dependence(model, features, columns, column, np):
    """Partial dependence: the average risk when every row takes the same value of one column (quantile grid)."""
    index = columns.index(column)
    values = features[:, index]
    grid = np.unique(np.nanquantile(values, np.linspace(0.02, 0.98, DEPENDENCE_POINTS)))
    points = []
    for value in grid:
        changed = features.copy()
        changed[:, index] = value
        points.append({'value': round(float(value), 2), 'risk': round(float(model.predict(changed).mean()), 4)})
    return points


def phase_of(second):
    return next(label for label, start, end in PHASES if second >= start and (end is None or second < end))


# Changes a player can be told to make, as (column, direction of the change towards the safe value). Every other change
# only describes the moment ("context"): the model is right that an ally at 465 meant a fight, but "stay away from
# your allies" or "have more levels" is not advice.
ADVICE = {('nearestAllyDistance', -1), ('alliesNear', 1), ('alliesWithinFightRange', 1), ('onEnemySide', -1),
          ('damageFromHeroesLastMinute', -1), ('teamObserversActive', 1),
          # Low health: back off and heal before the next fight. Being hit right now: step out.
          ('health', 1), ('healthShare', 1), ('damageFromHeroesLast6Seconds', -1),
          # Past your own creeps: stay behind the wave.
          ('aheadOfOwnWave', -1)}
ENEMY_REGIONS = {REGIONS.index('enemyBase'), REGIONS.index('enemyJungle')}


def kind_of(column, value, safe):
    if column == 'region':
        return 'advice' if value in ENEMY_REGIONS and safe not in ENEMY_REGIONS else 'context'
    return 'advice' if (column, 1 if safe > value else -1) in ADVICE else 'context'


def typical_safe(features, columns, np):
    """The typical value of each column in safe moments: the median, or the most common category."""
    typical = np.nanmedian(features, axis=0)
    for column in CATEGORICAL:
        index = columns.index(column)
        values, counts = np.unique(features[:, index], return_counts=True)
        typical[index] = values[np.argmax(counts)]
    return typical


def what_if(model, row, typical, columns, np):
    """For each actionable number: the risk if only that number had its typical safe value. The numbers whose change
    lowers the risk the most are what the model says made the moment dangerous (in the right direction)."""
    changes = [index for index, column in enumerate(columns)
               if column not in NOT_ACTIONABLE and not np.isnan(row[index]) and not np.isnan(typical[index])
               and row[index] != typical[index]]
    if not changes:
        return []
    batch = np.repeat(row[None, :], len(changes), axis=0)
    for line, index in enumerate(changes):
        batch[line, index] = typical[index]
    risk = float(model.predict(row[None, :])[0])
    changed = model.predict(batch)
    ranked = sorted(zip(changes, changed, strict=False), key=lambda pair: pair[1])
    return [{'column': columns[index], 'value': round(float(row[index]), 2), 'safeValue': round(float(typical[index]), 2),
             'riskIfSafe': round(float(other), 3),
             'kind': kind_of(columns[index], float(row[index]), float(typical[index]))}
            for index, other in ranked if other < risk][:STANDOUTS]


def explain_deaths(model, features, labels, groups, extra, focus, columns, np):
    """Per death of the focus heroes: the risk at each sample before it and, for the last one, what made it risky
    (what_if against safe moments of the same role and phase)."""
    risks = model.predict(features)
    roles = np.asarray([role or 'none' for role in extra['role']])
    # Safe moments of the same role in the same 5 minutes: a death at 2:00 is not compared with 8:00 (more wards, levels).
    windows = extra['seconds'] // REFERENCE_WINDOW_SECONDS
    hero_ids, died_at = extra['heroId'], extra['diedAtSeconds']
    references = {}
    deaths = []
    for match_id, hero_id in focus:
        rows = np.flatnonzero((groups == match_id) & (hero_ids == hero_id) & (labels == 1))
        for death in sorted({int(died_at[row]) for row in rows}):
            before = rows[died_at[rows] == death]
            last = before[np.argmax(extra['seconds'][before])]
            key = (roles[last], windows[last])
            if key not in references:
                references[key] = typical_safe(
                    features[(labels == 0) & (roles == key[0]) & (windows == key[1])], columns, np)
            deaths.append({
                'matchId': str(match_id), 'heroId': int(hero_id), 'diedAtSeconds': death, 'role': key[0],
                'phase': phase_of(int(extra['seconds'][last])),
                'riskBefore': [{'seconds': int(extra['seconds'][row]), 'risk': round(float(risks[row]), 3)}
                               for row in sorted(before, key=lambda row: extra['seconds'][row])],
                'standouts': what_if(model, features[last], references[key], columns, np),
            })
    return deaths


def explain(models_dir, export, output, partitions, focus=(), lib=None):
    lib = lib or ml()
    np = lib['np']
    models_dir, output = Path(models_dir), Path(output)
    require(not output.exists(), f'Output already exists: {output}')
    require(FINAL_TEST not in partitions and not any(split.startswith('reserved-') for split in partitions),
            'Explanations never open the final test or the reserved partitions')
    training = parse_json((models_dir / 'training.json').read_text(encoding='utf-8'))
    require(file_hash(models_dir / 'models.joblib') == training['modelsSha256'], 'models.joblib changed after training')
    require(export_identity(export) == training['export'], 'The export differs from the one the models were trained on')
    model = lib['joblib'].load(models_dir / 'models.joblib')[MODEL]
    # The model's own columns (training.json): explanations of older models read their own feature version.
    columns = training['columns'][MODEL]
    features, labels, groups, extra = load(export, partitions, columns, np)
    rng = np.random.default_rng(SEED)

    sample = rng.choice(len(labels), size=min(IMPORTANCE_ROWS, len(labels)), replace=False)
    ranked = importance(model, features[sample], labels[sample], columns, np, rng)
    small = sample[:DEPENDENCE_ROWS]
    # Partial dependence walks a numeric grid: categories (region) have no order to walk.
    actionable = [item['column'] for item in ranked
                  if item['column'] not in NOT_ACTIONABLE and item['column'] not in CATEGORICAL][:TOP]
    report = {
        'schemaVersion': 1, 'model': MODEL, 'partitions': list(partitions),
        'importance': ranked,
        'dependence': {column: dependence(model, features[small], columns, column, np) for column in actionable},
        'deaths': explain_deaths(model, features, labels, groups, extra, focus, columns, np),
        'humanReviewed': False,
    }
    output.mkdir(parents=True)
    with (output / 'explanations.json').open('x', encoding='utf-8') as stream:
        stream.write(dumps(report))
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml risk-explain')
    parser.add_argument('--models', required=True)
    parser.add_argument('--export', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--partition', action='append', required=True, help='Development or validation partitions')
    parser.add_argument('--focus', help='JSON file: [{"matchId": "...", "heroId": 1}, ...] whose deaths to explain')
    args = parser.parse_args(argv)
    try:
        focus = [(int(item['matchId']), int(item['heroId']))
                 for item in parse_json(Path(args.focus).read_text(encoding='utf-8-sig'))] if args.focus else []
        report = explain(args.models, args.export, args.output, args.partition, focus)
    except (AuditError, OSError, KeyError, ValueError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps({'importance': report['importance'][:8], 'deaths': len(report['deaths'])}))
    return 0
