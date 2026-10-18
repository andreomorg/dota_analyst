"""Single-host durable attempts and exclusive resources; leases never expire automatically."""
from contextlib import contextmanager
from datetime import datetime, timezone
import json
from pathlib import Path
import sqlite3
import uuid

from ..core.validation import AuditError, require


class PausePending(AuditError):
    """A requested pause must win a concurrent phase/completion transition."""


class RunStore:
    def __init__(self, path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.executescript('''
                CREATE TABLE IF NOT EXISTS runs (
                    id TEXT PRIMARY KEY, experiment TEXT NOT NULL, identity TEXT NOT NULL,
                    attempt TEXT NOT NULL, state TEXT NOT NULL, updated TEXT NOT NULL,
                    checkpoint TEXT, detail TEXT);
                CREATE TABLE IF NOT EXISTS resources (
                    id TEXT PRIMARY KEY, run_id TEXT NOT NULL, attempt TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS events (
                    sequence INTEGER PRIMARY KEY, run_id TEXT NOT NULL, attempt TEXT NOT NULL,
                    state TEXT NOT NULL, created TEXT NOT NULL);
            ''')

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=10, isolation_level=None)
        try:
            yield db
        finally:
            db.close()

    @contextmanager
    def transaction(self):
        with self.connect() as db:
            db.execute('BEGIN IMMEDIATE')
            try:
                yield db
                db.commit()
            except BaseException:
                db.rollback()
                raise

    def _event(self, db, run_id, attempt, state):
        db.execute('INSERT INTO events(run_id,attempt,state,created) VALUES (?,?,?,?)',
                   (run_id, attempt, state, datetime.now(timezone.utc).isoformat()))

    def start(self, run_id, experiment, identity, resource='gpu:0', initial_state='running'):
        require(all(isinstance(v, str) and v for v in (run_id, identity, resource)), 'Missing run identity')
        require(initial_state in ('running', 'preparing'), 'Invalid initial phase')
        attempt = uuid.uuid4().hex
        with self.transaction() as db:
            previous = db.execute('SELECT state,identity FROM runs WHERE id=?', (run_id,)).fetchone()
            require(previous is None or previous[0] in ('prepared', 'paused', 'failed'),
                    'Run is active, completed or needs investigation; no automatic takeover')
            require(previous is None or previous[1] == identity, 'Frozen run identity changed')
            require(db.execute('SELECT 1 FROM resources WHERE id=?', (resource,)).fetchone() is None,
                    'GPU resource already owned; paused or stale workers must be investigated')
            now = datetime.now(timezone.utc).isoformat()
            db.execute('INSERT INTO runs VALUES (?,?,?,?,?,?,NULL,NULL) ON CONFLICT(id) DO UPDATE SET '
                       'attempt=excluded.attempt,state=excluded.state,updated=excluded.updated,detail=NULL',
                       (run_id, str(Path(experiment).resolve()), identity, attempt, initial_state, now))
            db.execute('INSERT INTO resources VALUES (?,?,?)', (resource, run_id, attempt))
            self._event(db, run_id, attempt, initial_state)
        return attempt

    def get(self, run_id):
        with self.connect() as db:
            db.row_factory = sqlite3.Row
            row = db.execute('SELECT * FROM runs WHERE id=?', (run_id,)).fetchone()
            require(row is not None, 'Unknown run')
            return dict(row)

    def request_pause(self, run_id):
        with self.transaction() as db:
            row = db.execute('SELECT attempt,state FROM runs WHERE id=?', (run_id,)).fetchone()
            require(row is not None and row[1] in ('running', 'generating', 'evaluating', 'pause-requested'),
                    'Run is not executing')
            db.execute("UPDATE runs SET state='pause-requested',updated=? WHERE id=?",
                       (datetime.now(timezone.utc).isoformat(), run_id))
            self._event(db, run_id, row[0], 'pause-requested')

    def transition(self, run_id, attempt, state, checkpoint=None, detail=None):
        require(state in ('generating', 'evaluating', 'prepared', 'paused', 'completed', 'failed'), 'Invalid transition')
        terminal = state in ('prepared', 'paused', 'completed', 'failed')
        with self.transaction() as db:
            row = db.execute('SELECT state,attempt FROM runs WHERE id=?', (run_id,)).fetchone()
            require(row is not None and row[1] == attempt and row[0] in
                    ('preparing', 'running', 'generating', 'evaluating', 'pause-requested'), 'Stale attempt or terminal run')
            if row[0] == 'pause-requested' and state in ('generating', 'evaluating', 'completed'):
                raise PausePending('Pause requested before phase transition')
            allowed = {'preparing': {'prepared', 'failed'},
                       'running': {'generating', 'evaluating', 'prepared', 'paused', 'completed', 'failed'},
                       'generating': {'evaluating', 'paused', 'completed', 'failed'},
                       'evaluating': {'paused', 'completed', 'failed'}, 'pause-requested': {'paused', 'failed'}}
            require(state in allowed[row[0]], 'Invalid lifecycle transition')
            require(not (row[0] == 'pause-requested' and state not in ('paused', 'failed')), 'Pause must be acknowledged')
            require(state != 'paused' or checkpoint is not None, 'Pause requires a verified checkpoint')
            db.execute('UPDATE runs SET state=?,updated=?,checkpoint=?,detail=? WHERE id=? AND attempt=?',
                       (state, datetime.now(timezone.utc).isoformat(), json.dumps(checkpoint) if checkpoint else None,
                        detail, run_id, attempt))
            self._event(db, run_id, attempt, state)
            if terminal:
                db.execute('DELETE FROM resources WHERE run_id=? AND attempt=?', (run_id, attempt))
