"""Reparse kept replays, with a resumable JSON-lines log (standard library only).

Generate IDs from the LOCAL database, from the repository root:
    New-Item -ItemType Directory -Force work/replay-refresh
    docker compose exec -T db psql -U dotaanalyst -d dotaanalyst -Atc "SELECT match_id FROM replay_documents ORDER BY match_id" | Set-Content -Encoding utf8 work/replay-refresh/match-ids.txt
Set MAINTENANCE_ENABLED=true and MAINTENANCE_API_KEY (at least 16 characters), then:
    docker compose run --rm refresh
MATCH_IDS: UTF-8 text file, one positive match ID per line. LOG: resumable output.
MAINTENANCE_API_KEY takes precedence; COACH_ML_API_KEY is accepted as fallback.
"""
import json
import os
import sys
import threading
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

API = os.environ.get('API_URL', 'http://api:8080/').rstrip('/') + '/'
KEY = os.environ.get('MAINTENANCE_API_KEY') or os.environ.get('COACH_ML_API_KEY', '')
MATCH_IDS = os.environ.get('MATCH_IDS', '/work/replay-refresh/match-ids.txt')
LOG = os.environ.get('LOG', '/work/replay-refresh/refresh-log.jsonl')
FINAL = {'refreshed', 'noReplay', 'expired', 'failed'}
WAITS = {'parserBusy': 15, 'parserUnavailable': 120, 'sourceUnavailable': 120, 'http': 60}
# The parser takes MAX_CONCURRENT_PARSES=2 at once (compose): two workers keep both busy while downloads wait.
WORKERS = int(os.environ.get('WORKERS', '1'))
# Download-only workers (POST .../prefetch: the parser keeps the replay in its cache): they run ahead of the parse
# workers, at most PREFETCH_AHEAD matches, so the parse slots parse instead of waiting on Valve. 0 = off.
PREFETCH_WORKERS = int(os.environ.get('PREFETCH_WORKERS', '0'))
PREFETCH_AHEAD = int(os.environ.get('PREFETCH_AHEAD', '12'))


def done():
    seen = {}
    if os.path.exists(LOG):
        with open(LOG, encoding='utf-8') as stream:
            for line in stream:
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError:
                    if line.endswith('\n'):
                        raise
                    break  # An interrupted final write is retryable.
                seen[int(entry['matchId'])] = entry['status']
    return {match for match, status in seen.items() if status in FINAL}


def refresh(match_id, action='refresh'):
    request = urllib.request.Request(f'{API}api/internal/replays/{match_id}/{action}', method='POST', data=b'',
                                     headers={'Authorization': 'Bearer ' + KEY})
    try:
        with urllib.request.urlopen(request, timeout=900) as response:
            return json.loads(response.read())['status']
    except urllib.error.HTTPError as exc:
        return f'http{exc.code}'
    except (urllib.error.URLError, TimeoutError, OSError, ValueError, KeyError):
        return 'http'


def main():
    if not KEY.strip():
        raise ValueError('Set MAINTENANCE_API_KEY (or COACH_ML_API_KEY).')
    if WORKERS < 1 or PREFETCH_WORKERS < 0 or PREFETCH_AHEAD < 1:
        raise ValueError('Invalid worker or prefetch limits.')
    ids = set()
    with open(MATCH_IDS, encoding='utf-8-sig') as stream:
        for number, line in enumerate(stream, 1):
            value = line.strip()
            if not value:
                continue
            if not value.isascii() or not value.isdecimal() or not 0 < int(value) <= 9223372036854775807:
                raise ValueError(f'Invalid match ID at line {number}.')
            ids.add(int(value))
    ids = sorted(ids)
    os.makedirs(os.path.dirname(os.path.abspath(LOG)), exist_ok=True)
    skip = done()
    # Drop an unterminated last record before appending after an interrupted process.
    if os.path.exists(LOG) and os.path.getsize(LOG):
        with open(LOG, 'rb+') as stream:
            stream.seek(-1, os.SEEK_END)
            if stream.read(1) != b'\n':
                stream.seek(0)
                data = stream.read()
                stream.seek(data.rfind(b'\n') + 1)
                stream.truncate()
    todo = [match for match in ids if match not in skip]
    print(json.dumps({'total': len(ids), 'alreadyDone': len(ids) - len(todo), 'todo': len(todo), 'workers': WORKERS,
                      'prefetchWorkers': PREFETCH_WORKERS}), flush=True)
    lock, finished = threading.Lock(), [0]
    # One event per match: set when its download finished (or failed, or prefetching is off).
    ready = {match: threading.Event() for match in todo}
    ahead = threading.Semaphore(PREFETCH_AHEAD)

    def prefetch(match_id):
        try:
            for _ in range(6):
                status = refresh(match_id, 'prefetch')
                # Done, or a final answer the refresh will repeat (expired, no replay...): either way, stop.
                if status in FINAL or status == 'prefetched':
                    break
                time.sleep(WAITS.get(status, WAITS['http']))
        finally:
            ready[match_id].set()

    def prefetch_all():
        with ThreadPoolExecutor(max_workers=PREFETCH_WORKERS) as downloads:
            for match_id in todo:
                ahead.acquire()  # released when a parse worker finishes a match
                downloads.submit(prefetch, match_id)

    if PREFETCH_WORKERS > 0:
        threading.Thread(target=prefetch_all, daemon=True).start()
    else:
        for event in ready.values():
            event.set()

    with open(LOG, 'a', encoding='utf-8') as log:
        def one(match_id):
            # Wait for its download (bounded: a stuck prefetch never blocks the batch; the refresh downloads itself).
            ready[match_id].wait(timeout=600)
            for attempt in range(6):
                started = time.time()
                status = refresh(match_id)
                if status in FINAL:
                    break
                time.sleep(WAITS.get(status, WAITS['http']))
            with lock:
                log.write(json.dumps({'matchId': match_id, 'status': status, 'seconds': round(time.time() - started),
                                      'at': time.strftime('%Y-%m-%dT%H:%M:%S')}) + '\n')
                log.flush()
                finished[0] += 1
                if finished[0] % 25 == 0:
                    print(json.dumps({'done': finished[0], 'of': len(todo)}), flush=True)
            if PREFETCH_WORKERS > 0:
                ahead.release()
            time.sleep(2)  # a little room for the site's own parses and OpenDota's quota

        with ThreadPoolExecutor(max_workers=WORKERS) as pool:
            list(pool.map(one, todo))


if __name__ == '__main__':
    sys.exit(main())
