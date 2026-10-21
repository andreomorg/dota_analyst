"""Train the four death-risk models on the development partitions (scikit-learn, CPU). Never reads validation/test."""
import argparse
import copy
import json
import sys
from pathlib import Path

from ..core.validation import AuditError, require
from ..hashing import file_hash
from .data import (
    CATEGORICAL,
    DEVELOPMENT,
    HIDDEN_BY_VERSION,
    SIMPLE,
    VISIBLE_BY_VERSION,
    dumps,
    export_identity,
    feature_version,
    load,
)

FIT, TUNE, CALIBRATE = 0.70, 0.15, 0.15
ITERATIONS = [100, 200, 400, 800]
SEED = 20261020
# Far beyond any fight: what "no ally alive" means to the simple model (it cannot take a missing value).
NO_ALLY_DISTANCE = 20000.0


def ml():
    """scikit-learn and numpy, imported only by the commands that train or evaluate (the CLI stays light)."""
    try:
        import joblib
        import numpy as np
        import sklearn
        from sklearn.ensemble import HistGradientBoostingClassifier
        from sklearn.isotonic import IsotonicRegression
        from sklearn.linear_model import LogisticRegression
        from sklearn.metrics import log_loss
        from sklearn.pipeline import make_pipeline
        from sklearn.preprocessing import StandardScaler
    except ImportError as exc:
        raise AuditError('Install the risk extra: pip install "./coach-ml[risk]"') from exc
    return {'joblib': joblib, 'np': np, 'version': sklearn.__version__, 'HGB': HistGradientBoostingClassifier,
            'Isotonic': IsotonicRegression, 'Logistic': LogisticRegression, 'log_loss': log_loss,
            'pipeline': make_pipeline, 'Scaler': StandardScaler}


def split_matches(groups, np, seed=SEED):
    """Masks (fit, tune, calibrate) by whole match: a match never lands on two sides."""
    matches = np.unique(groups)
    require(len(matches) >= 10, 'Too few matches to split')
    shuffled = np.random.default_rng(seed).permutation(matches)
    fit_end, tune_end = round(len(shuffled) * FIT), round(len(shuffled) * (FIT + TUNE))
    parts = [set(shuffled[:fit_end].tolist()), set(shuffled[fit_end:tune_end].tolist()), set(shuffled[tune_end:].tolist())]
    return [np.isin(groups, list(part)) for part in parts]


class Calibrated:
    """A fitted model plus an isotonic map from its scores to observed rates (fitted on separate matches)."""

    def __init__(self, model, isotonic):
        self.model, self.isotonic = model, isotonic

    def predict(self, features):
        return self.isotonic.predict(self.model.predict_proba(features)[:, 1])


class Constant:
    def __init__(self, rate):
        self.rate = float(rate)

    def predict(self, features):
        import numpy as np
        return np.full(len(features), self.rate)


def simple_features(features, visible, np):
    """The SIMPLE columns out of a visible matrix whose columns are `visible` (any feature version)."""
    nearest = features[:, visible.index('nearestAllyDistance')]
    nearest = np.where(np.isnan(nearest), NO_ALLY_DISTANCE, nearest)
    return np.column_stack([nearest, features[:, visible.index('onEnemySide')], features[:, visible.index('seconds')]])


def boosted(lib, features, labels, masks, columns, choices):
    """Boosting with the number of iterations picked on the tune matches, then calibrated on the calibrate matches."""
    fit, tune, calibrate = masks
    categorical = [column in CATEGORICAL for column in columns]
    best = None
    # warm_start: each larger max_iter continues the same trees instead of starting again; the best is copied.
    model = lib['HGB'](learning_rate=0.05, max_leaf_nodes=31, min_samples_leaf=200, l2_regularization=1.0,
                       early_stopping=False, categorical_features=categorical, random_state=SEED, warm_start=True)
    for iterations in sorted(choices):
        model.set_params(max_iter=iterations)
        model.fit(features[fit], labels[fit])
        loss = lib['log_loss'](labels[tune], model.predict_proba(features[tune])[:, 1])
        if best is None or loss < best[0]:
            best = (loss, iterations, copy.deepcopy(model))
    loss, iterations, model = best
    isotonic = lib['Isotonic'](out_of_bounds='clip', y_min=0.0, y_max=1.0)
    isotonic.fit(model.predict_proba(features[calibrate])[:, 1], labels[calibrate])
    return Calibrated(model, isotonic), {'iterations': iterations, 'tuneLogLoss': round(float(loss), 5)}


def train(export, output, lib=None, log=print, iterations=ITERATIONS):
    lib = lib or ml()
    np = lib['np']
    output = Path(output)
    require(not output.exists(), f'Output already exists: {output}')
    version = feature_version(export)
    visible_columns = VISIBLE_BY_VERSION[version]
    columns = visible_columns + HIDDEN_BY_VERSION[version]
    partitions = [split for split in DEVELOPMENT if split in export_identity(export)]
    require(partitions, 'The export has no development partition')
    features, labels, groups, _ = load(export, partitions, columns, np)
    masks = split_matches(groups, np)
    fit, tune, calibrate = masks
    visible = features[:, :len(visible_columns)]
    log(json.dumps({'rows': int(len(labels)), 'matches': int(len(np.unique(groups))), 'deathRate': round(float(labels.mean()), 5)}))

    models, details = {}, {}
    models['constant'] = Constant(labels[fit].mean())
    simple = simple_features(visible, visible_columns, np)
    logistic = lib['pipeline'](lib['Scaler'](), lib['Logistic'](max_iter=1000))
    logistic.fit(simple[fit], labels[fit])
    isotonic = lib['Isotonic'](out_of_bounds='clip', y_min=0.0, y_max=1.0)
    isotonic.fit(logistic.predict_proba(simple[calibrate])[:, 1], labels[calibrate])
    models['logistic'] = Calibrated(logistic, isotonic)
    models['boostedVisible'], details['boostedVisible'] = boosted(lib, visible, labels, masks, visible_columns, iterations)
    log(json.dumps({'trained': 'boostedVisible', **details['boostedVisible']}))
    models['boostedOmniscient'], details['boostedOmniscient'] = boosted(lib, features, labels, masks, columns, iterations)
    log(json.dumps({'trained': 'boostedOmniscient', **details['boostedOmniscient']}))

    output.mkdir(parents=True)
    lib['joblib'].dump(models, output / 'models.joblib')
    metadata = {
        'schemaVersion': 1,
        'export': export_identity(export),
        'featureVersion': version,
        'trainedOn': partitions,
        # Evaluation and explanations read the columns from here, never from the current code's lists.
        'columns': {'constant': [], 'logistic': SIMPLE, 'boostedVisible': visible_columns, 'boostedOmniscient': columns},
        'split': {'fit': FIT, 'tune': TUNE, 'calibrate': CALIBRATE, 'seed': SEED,
                  'rows': {'fit': int(fit.sum()), 'tune': int(tune.sum()), 'calibrate': int(calibrate.sum())}},
        'details': details,
        'iterationChoices': sorted(iterations),
        'scikitLearn': lib['version'],
        'humanReviewed': False,
    }
    metadata['modelsSha256'] = file_hash(output / 'models.joblib')
    # Written last: a folder without it is an interrupted training.
    with (output / 'training.json').open('x', encoding='utf-8') as stream:
        stream.write(dumps(metadata))
    return metadata


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml risk-train')
    parser.add_argument('--export', required=True, help='Folder written by risk-export')
    parser.add_argument('--output', required=True, help='New folder for the models')
    parser.add_argument('--iterations', type=int, nargs='+', default=ITERATIONS,
                        help='Boosting sizes to try (picked on the tune matches, never on validation)')
    args = parser.parse_args(argv)
    try:
        require(all(value > 0 for value in args.iterations), 'Iterations must be positive')
        metadata = train(args.export, args.output, iterations=args.iterations)
    except (AuditError, OSError) as exc:
        print(json.dumps({'error': str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps({'details': metadata['details'], 'rows': metadata['split']['rows']}))
    return 0
