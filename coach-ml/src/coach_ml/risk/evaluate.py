"""Evaluate saved death-risk models on validation (or, once, on the final test) and write a report."""
import argparse
import json
import sys
from pathlib import Path

from ..core.json import parse_json
from ..core.validation import AuditError, require
from ..hashing import file_hash
from .data import FINAL_TEST, VALIDATION, dumps, export_identity, load
from .train import ml, simple_features

PHASES = [('0-10', 0, 600), ('10-20', 600, 1200), ('20+', 1200, None)]
CALIBRATION_BINS = 10


def metrics(labels, predicted, lib):
    """PR-AUC (main: deaths are rare), ROC-AUC, Brier and log loss; None where a group has a single class."""
    from sklearn.metrics import average_precision_score, brier_score_loss, roc_auc_score
    np = lib['np']
    both = 0 < labels.sum() < len(labels)
    clipped = np.clip(predicted, 1e-6, 1 - 1e-6)
    return {
        'rows': int(len(labels)),
        'deathRate': round(float(labels.mean()), 5),
        'prAuc': round(float(average_precision_score(labels, predicted)), 4) if both else None,
        'rocAuc': round(float(roc_auc_score(labels, predicted)), 4) if both else None,
        'brier': round(float(brier_score_loss(labels, predicted)), 5),
        'logLoss': round(float(lib['log_loss'](labels, clipped, labels=[0, 1])), 5),
    }


def calibration(labels, predicted, np, bins=CALIBRATION_BINS):
    """Equal-count bins by predicted risk: mean predicted vs observed death rate ("when it says 30%, ~30% die?")."""
    order = np.argsort(predicted, kind='stable')
    return [{'predicted': round(float(predicted[part].mean()), 4), 'observed': round(float(labels[part].mean()), 4),
             'rows': int(len(part))}
            for part in np.array_split(order, bins) if len(part)]


def evaluate(models_dir, export, output, partitions=VALIDATION, final=False, lib=None):
    lib = lib or ml()
    np = lib['np']
    models_dir, output = Path(models_dir), Path(output)
    require(not output.exists(), f'Output already exists: {output}')
    require((FINAL_TEST in partitions) == final, 'The final test needs --final, and --final only evaluates it')
    require(not any(split.startswith('reserved-') for split in partitions), 'Reserved partitions are never evaluated')
    training = parse_json((models_dir / 'training.json').read_text(encoding='utf-8'))
    require(file_hash(models_dir / 'models.joblib') == training['modelsSha256'], 'models.joblib changed after training')
    require(export_identity(export) == training['export'], 'The export differs from the one the models were trained on')
    models = lib['joblib'].load(models_dir / 'models.joblib')

    # The models' own columns (training.json), so models of an older feature version still read their export.
    visible_columns, columns = training['columns']['boostedVisible'], training['columns']['boostedOmniscient']
    features, labels, groups, extra = load(export, partitions, columns, np)
    visible = features[:, :len(visible_columns)]
    inputs = {'constant': visible, 'logistic': simple_features(visible, visible_columns, np), 'boostedVisible': visible,
              'boostedOmniscient': features}
    if 'boostedVisibleHero' in training['columns']:
        hero_columns = training['columns']['boostedVisibleHero']
        source = {column: features[:, index] for index, column in enumerate(columns)}
        source['heroId'] = extra['heroId']
        inputs['boostedVisibleHero'] = np.column_stack([source[column] for column in hero_columns])
    roles = np.asarray([role or 'none' for role in extra['role']])
    seconds = extra['seconds']

    report = {'schemaVersion': 1, 'partitions': list(partitions), 'final': final,
              'matches': int(len(np.unique(groups))), 'models': {},
              'note': 'validation-new is not the final test' if not final else 'final test: evaluated once'}
    for name in inputs:
        predicted = np.asarray(models[name].predict(inputs[name]), dtype=float)
        by_phase = {label: metrics(labels[mask], predicted[mask], lib)
                    for label, start, end in PHASES
                    for mask in [(seconds >= start) & (seconds < end if end else True)] if mask.any()}
        by_role = {role: metrics(labels[roles == role], predicted[roles == role], lib) for role in sorted(set(roles))}
        report['models'][name] = {'overall': metrics(labels, predicted, lib), 'byPhase': by_phase, 'byRole': by_role,
                                  'calibration': calibration(labels, predicted, np)}

    output.mkdir(parents=True)
    (output / 'report.md').write_text(table(report), encoding='utf-8')
    # Written last: a folder without it is an interrupted evaluation.
    with (output / 'report.json').open('x', encoding='utf-8') as stream:
        stream.write(dumps(report))
    return report


def table(report):
    lines = [f"Partições: {', '.join(report['partitions'])} — {report['matches']} partidas. {report['note']}.", '',
             '| Modelo | PR-AUC | ROC-AUC | Brier | Log loss |', '|---|---:|---:|---:|---:|']
    for name in report['models']:
        values = report['models'][name]['overall']
        lines.append(f"| {name} | {values['prAuc']} | {values['rocAuc']} | {values['brier']} | {values['logLoss']} |")
    return '\n'.join(lines) + '\n'


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml risk-evaluate')
    parser.add_argument('--models', required=True, help='Folder written by risk-train')
    parser.add_argument('--export', required=True, help='The same export the models were trained on')
    parser.add_argument('--output', required=True, help='New folder for the report')
    parser.add_argument('--final', action='store_true', help='Evaluate the final test (test-new) instead, once')
    args = parser.parse_args(argv)
    try:
        report = evaluate(args.models, args.export, args.output, [FINAL_TEST] if args.final else VALIDATION, args.final)
    except (AuditError, OSError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(table(report), end='')
    return 0
