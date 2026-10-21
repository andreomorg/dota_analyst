"""Second model: where a hero goes in the next 30 s (a map region, or dead), learned from what players of the export
did. Trained on high-level players, its probabilities say what they did in situations like this (CPU, scikit-learn)."""
import argparse
import copy
import json
import sys
from pathlib import Path

from ..core.json import parse_json
from ..core.validation import AuditError, require
from ..hashing import file_hash
from .data import (
    CATEGORICAL,
    DESTINATIONS,
    DEVELOPMENT,
    FINAL_TEST,
    INTENT,
    VALIDATION,
    VISIBLE_BY_VERSION,
    dumps,
    export_identity,
    feature_version,
    load,
)
from .evaluate import PHASES
from .train import SEED, ml, split_matches

KIND = 'next-region'
ITERATIONS = [100, 200, 400]
MINUTES_PER_BUCKET = 5
# Laplace smoothing of the frequency baselines: a destination never seen still gets a little probability.
SMOOTHING = 1.0
MODELS = ['transition', 'roleMinute', 'boosted']


class FrequencyModel:
    """P(destination | key) counted on the fit matches, smoothed; an unseen key falls back to the overall rates."""

    def __init__(self, keys, labels, np):
        self.classes = len(DESTINATIONS)
        overall = np.bincount(labels, minlength=self.classes) + SMOOTHING
        self.overall = overall / overall.sum()
        # One pass counting: a mask per key over millions of rows would be slow.
        counts = {}
        for key, label in zip(keys, labels.tolist(), strict=False):
            counts.setdefault(key, [0] * self.classes)[label] += 1
        self.table = {key: (np.asarray(row) + SMOOTHING) / (sum(row) + SMOOTHING * self.classes) for key, row in counts.items()}

    def predict_proba(self, keys):
        import numpy as np
        return np.vstack([self.table.get(key, self.overall) for key in keys])


class AllClasses:
    """A fitted classifier whose probabilities always have one column per DESTINATIONS entry, in order: a class missing
    from the training rows gets 0 (scikit-learn would leave its column out)."""

    def __init__(self, model):
        self.model = model

    def predict_proba(self, features):
        import numpy as np
        raw = self.model.predict_proba(features)
        full = np.zeros((len(features), len(DESTINATIONS)))
        full[:, self.model.classes_.astype(int)] = raw
        return full


def transition_keys(features, columns):
    return [int(value) for value in features[:, columns.index('region')]]


def role_minute_keys(features, columns):
    roles, seconds = features[:, columns.index('role')], features[:, columns.index('seconds')]
    return [(-1 if role != role else int(role), int(second // (60 * MINUTES_PER_BUCKET))) for role, second in zip(roles, seconds, strict=False)]


def train(export, output, lib=None, log=print, iterations=ITERATIONS):
    lib = lib or ml()
    np = lib['np']
    output = Path(output)
    require(not output.exists(), f'Output already exists: {output}')
    # The visible columns without the player's own orders: the destination clicked now would give the answer away.
    columns = [column for column in VISIBLE_BY_VERSION[feature_version(export)] if column not in INTENT]
    partitions = [split for split in DEVELOPMENT if split in export_identity(export)]
    require(partitions, 'The export has no development partition')
    features, labels, groups, _ = load(export, partitions, columns, np, kind=KIND)
    fit, tune, calibrate = split_matches(groups, np)
    log(json.dumps({'rows': int(len(labels)), 'matches': int(len(np.unique(groups)))}))

    models = {
        'transition': FrequencyModel(transition_keys(features[fit], columns), labels[fit], np),
        'roleMinute': FrequencyModel(role_minute_keys(features[fit], columns), labels[fit], np),
    }
    categorical = [column in CATEGORICAL for column in columns]
    model = lib['HGB'](learning_rate=0.05, max_leaf_nodes=31, min_samples_leaf=200, l2_regularization=1.0,
                       early_stopping=False, categorical_features=categorical, random_state=SEED, warm_start=True)
    best = None
    for count in sorted(iterations):
        model.set_params(max_iter=count)
        model.fit(features[fit], labels[fit])
        probabilities = np.clip(AllClasses(model).predict_proba(features[tune]), 1e-6, 1)
        loss = lib['log_loss'](labels[tune], probabilities, labels=list(range(len(DESTINATIONS))))
        if best is None or loss < best[0]:
            best = (loss, count, copy.deepcopy(model))
    models['boosted'] = AllClasses(best[2])
    details = {'iterations': best[1], 'tuneLogLoss': round(float(best[0]), 5),
               # Multiclass calibration is left to the report (per class); the calibrate matches stay unused for now.
               'calibrateRowsUnused': int(calibrate.sum())}
    log(json.dumps({'trained': 'boosted', **details}))

    output.mkdir(parents=True)
    lib['joblib'].dump(models, output / 'models.joblib')
    metadata = {
        'schemaVersion': 1, 'kind': KIND, 'export': export_identity(export), 'featureVersion': feature_version(export),
        'trainedOn': partitions, 'columns': columns, 'classes': DESTINATIONS, 'details': details,
        'iterationChoices': sorted(iterations), 'scikitLearn': lib['version'], 'humanReviewed': False,
        'modelsSha256': file_hash(output / 'models.joblib'),
    }
    with (output / 'training.json').open('x', encoding='utf-8') as stream:
        stream.write(dumps(metadata))
    return metadata


def scores(labels, probabilities, lib):
    """Top-1 and top-3 accuracy, log loss, and per class the mean predicted probability vs the observed rate."""
    np = lib['np']
    ranked = np.argsort(-probabilities, axis=1)
    return {
        'rows': int(len(labels)),
        'top1': round(float((ranked[:, 0] == labels).mean()), 4),
        'top3': round(float((ranked[:, :3] == labels[:, None]).any(axis=1).mean()), 4),
        'logLoss': round(float(lib['log_loss'](labels, np.clip(probabilities, 1e-6, 1), labels=list(range(len(DESTINATIONS))))), 5),
        'calibration': {name: {'predicted': round(float(probabilities[:, index].mean()), 4),
                               'observed': round(float((labels == index).mean()), 4)}
                        for index, name in enumerate(DESTINATIONS)},
    }


def evaluate(models_dir, export, output, partitions=VALIDATION, final=False, lib=None):
    lib = lib or ml()
    np = lib['np']
    models_dir, output = Path(models_dir), Path(output)
    require(not output.exists(), f'Output already exists: {output}')
    require((FINAL_TEST in partitions) == final, 'The final test needs --final, and --final only evaluates it')
    require(not any(split.startswith('reserved-') for split in partitions), 'Reserved partitions are never evaluated')
    training = parse_json((models_dir / 'training.json').read_text(encoding='utf-8'))
    require(training.get('kind') == KIND, 'These are not next-region models')
    require(file_hash(models_dir / 'models.joblib') == training['modelsSha256'], 'models.joblib changed after training')
    require(export_identity(export) == training['export'], 'The export differs from the one the models were trained on')
    models = lib['joblib'].load(models_dir / 'models.joblib')
    columns = training['columns']
    features, labels, groups, extra = load(export, partitions, columns, np, kind=KIND)
    seconds = extra['seconds']
    roles = np.asarray([role or 'none' for role in extra['role']])
    inputs = {'transition': transition_keys(features, columns), 'roleMinute': role_minute_keys(features, columns),
              'boosted': features}
    report = {'schemaVersion': 1, 'kind': KIND, 'partitions': list(partitions), 'final': final,
              'matches': int(len(np.unique(groups))), 'classes': DESTINATIONS, 'models': {}}
    for name in MODELS:
        probabilities = np.asarray(models[name].predict_proba(inputs[name]), dtype=float)
        report['models'][name] = {
            'overall': scores(labels, probabilities, lib),
            'byPhase': {label: scores(labels[mask], probabilities[mask], lib)
                        for label, start, end in PHASES
                        for mask in [(seconds >= start) & (seconds < end if end else True)] if mask.any()},
            'byRole': {role: scores(labels[roles == role], probabilities[roles == role], lib) for role in sorted(set(roles))},
        }
    output.mkdir(parents=True)
    with (output / 'report.json').open('x', encoding='utf-8') as stream:
        stream.write(dumps(report))
    return report


def train_main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml region-train')
    parser.add_argument('--export', required=True, help='Folder written by risk-export --kind next-region')
    parser.add_argument('--output', required=True, help='New folder for the models')
    parser.add_argument('--iterations', type=int, nargs='+', default=ITERATIONS)
    args = parser.parse_args(argv)
    try:
        require(all(value > 0 for value in args.iterations), 'Iterations must be positive')
        metadata = train(args.export, args.output, iterations=args.iterations)
    except (AuditError, OSError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps(metadata['details']))
    return 0


def evaluate_main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml region-evaluate')
    parser.add_argument('--models', required=True)
    parser.add_argument('--export', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--final', action='store_true', help='Evaluate the final test (test-new) instead, once')
    args = parser.parse_args(argv)
    try:
        report = evaluate(args.models, args.export, args.output, [FINAL_TEST] if args.final else VALIDATION, args.final)
    except (AuditError, OSError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps({name: {key: values['overall'][key] for key in ('top1', 'top3', 'logLoss')}
                      for name, values in report['models'].items()}))
    return 0
