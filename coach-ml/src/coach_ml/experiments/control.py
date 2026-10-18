"""Cooperative control for explicitly managed future runs; no process or Docker manipulation."""
import argparse
import json
import os

from ..core.validation import require
from .runs import RunStore, PausePending


TrainingPaused = PausePending


def current():
    path, run, attempt = (os.environ.get(key) for key in
                          ('COACH_ML_RUN_STORE', 'COACH_ML_RUN_ID', 'COACH_ML_ATTEMPT'))
    if not run and not attempt:
        return None
    require(path and run and attempt, 'Incomplete managed run context')
    return RunStore(path), run, attempt


def pause_requested():
    context = current()
    if context is None:
        return False
    store, run, attempt = context
    row = store.get(run)
    require(row['attempt'] == attempt, 'Worker lost attempt ownership')
    return row['state'] == 'pause-requested'


def phase(state):
    if pause_requested():
        raise TrainingPaused('Pause requested at phase boundary')
    context = current()
    if context:
        store, run, attempt = context
        store.transition(run, attempt, state)


def main(argv=None):
    parser = argparse.ArgumentParser(prog='coach-ml run-control')
    parser.add_argument('--store', required=True)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--action', choices=('status', 'pause'), default='status')
    args = parser.parse_args(argv)
    store = RunStore(args.store)
    if args.action == 'pause':
        store.request_pause(args.run_id)
    print(json.dumps(store.get(args.run_id), ensure_ascii=False))
    return 0
